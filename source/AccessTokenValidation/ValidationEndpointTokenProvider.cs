﻿/*
 * Copyright 2015 Dominick Baier, Brock Allen
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Infrastructure;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Thinktecture.IdentityServer.AccessTokenValidation
{
    internal class ValidationEndpointTokenProvider : AuthenticationTokenProvider
    {
        private readonly HttpClient _client;
        private readonly string _tokenValidationEndpoint;
        private readonly IdentityServerBearerTokenAuthenticationOptions _options;

        public ValidationEndpointTokenProvider(IdentityServerBearerTokenAuthenticationOptions options)
        {
            var baseAddress = options.Authority.EnsureTrailingSlash();
            baseAddress += "connect/accesstokenvalidation";
            _tokenValidationEndpoint = baseAddress + "?token={0}";

            var handler = options.BackchannelHttpHandler ?? new WebRequestHandler();

            if (options.BackchannelCertificateValidator != null)
            {
                // Set the cert validate callback
                var webRequestHandler = handler as WebRequestHandler;
                if (webRequestHandler == null)
                {
                    throw new InvalidOperationException("Invalid certificate validator");
                }

                webRequestHandler.ServerCertificateValidationCallback = options.BackchannelCertificateValidator.Validate;
            }

            _client = new HttpClient(handler);
            _options = options;
        }

        public override async Task ReceiveAsync(AuthenticationTokenReceiveContext context)
        {
            if (_options.EnableValidationResultCache)
            {
                var cachedClaims = await _options.ValidationResultCache.GetAsync(context.Token);
                if (cachedClaims != null)
                {
                    SetAuthenticationTicket(context, cachedClaims);
                    return;
                }
            }

            var url = string.Format(_tokenValidationEndpoint, context.Token);

            var response = await _client.GetAsync(url);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return;
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);

            var claims = new List<Claim>();

            foreach (var item in dictionary)
            {
                var values = item.Value as IEnumerable<object>;

                if (values == null)
                {
                    claims.Add(new Claim(item.Key, item.Value.ToString()));
                }
                else
                {
                    foreach (var value in values)
                    {
                        claims.Add(new Claim(item.Key, value.ToString()));
                    }
                }
            }

            if (_options.EnableValidationResultCache)
            {
                await _options.ValidationResultCache.AddAsync(context.Token, claims);
            }

            SetAuthenticationTicket(context, claims);
        }

        private void SetAuthenticationTicket(AuthenticationTokenReceiveContext context, IEnumerable<Claim> claims)
        {
            var id = new ClaimsIdentity(
                            claims,
                            _options.AuthenticationType,
                            _options.NameClaimType,
                            _options.RoleClaimType);

            context.SetTicket(new AuthenticationTicket(id, new AuthenticationProperties()));
        }
    }
}