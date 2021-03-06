﻿using RaLibrary.Data.Managers;
using RaLibrary.Filters.Results;
using RaLibrary.Utilities;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Filters;


namespace RaLibrary.Filters
{
    public class RaAuthenticationAttribute : Attribute, IAuthenticationFilter
    {
        private static readonly string Scheme = "Bearer";
        private static readonly HttpClient httpClient = new HttpClient();

        private IAdministratorManager administrators = new AdministratorManager();
        private string realm = "ralibrary_resources";

        public string Realm
        {
            get
            {
                return realm;
            }
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    realm = value;
                }
            }
        }

        public bool AllowMultiple
        {
            get {
                return false;
            }
        }

        public async Task AuthenticateAsync(HttpAuthenticationContext context, CancellationToken cancellationToken)
        {
            HttpRequestMessage request = context.Request;
            AuthenticationHeaderValue authorization = request.Headers.Authorization;

            if (authorization == null)
            {
                // No authentication was attempted (for this authentication method).
                // Do not set either Principal (which would indicate success) or ErrorResult (indicating an error).
                return;
            }

            if (authorization.Scheme != Scheme)
            {
                // No authentication was attempted (for this authentication method).
                // Do not set either Principal (which would indicate success) or ErrorResult (indicating an error).
                return;
            }

            if (string.IsNullOrEmpty(authorization.Parameter))
            {
                // Authentication was attempted but failed. Set ErrorResult to indicate an error.
                context.ErrorResult = new AuthenticationFailureResult(request);
                return;
            }

            // Post token to Authentication Server of RA
            string tokenValidationEndpoint = ConfigurationManager.AppSettings.Get("TokenValidationEndpoint");
            Dictionary<string, string> values = new Dictionary<string, string>
                {
                   { "IdToken", authorization.Parameter },
                };
            FormUrlEncodedContent content = new FormUrlEncodedContent(values);
            HttpResponseMessage response = await httpClient.PostAsync(tokenValidationEndpoint, content);

            string email = string.Empty;
            string name = string.Empty;
            if (response.StatusCode == HttpStatusCode.NoContent) // succeeded
            {
                Jwt jwtInfo = Jwt.GetJwtFromRequestHeader(request);
                if (jwtInfo != null)
                {
                    JwtPayload jwtPayload = jwtInfo.Payload;

                    if (jwtPayload != null)
                    {
                        email = jwtPayload.Email;
                        name = jwtPayload.Name;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                // Authentication was attempted but failed. Set ErrorResult to indicate an error.
                context.ErrorResult = new AuthenticationFailureResult(request);
                return;
            }

            IList<Claim> claimCollection = new List<Claim>
            {
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.Email, email)
            };

            if (administrators.IsAdministrator(email))
            {
                claimCollection.Add(new Claim(ClaimTypes.Role, RoleTypes.Administrators));
                claimCollection.Add(new Claim(ClaimTypes.Role, RoleTypes.NormalUsers));
            }
            else
            {
                claimCollection.Add(new Claim(ClaimTypes.Role, RoleTypes.NormalUsers));
            }

            ClaimsIdentity claimIdentity = new ClaimsIdentity(claimCollection, Scheme);
            ClaimsPrincipal principal = new ClaimsPrincipal(claimIdentity);
            if (principal == null)
            {
                // Authentication was attempted but failed. Set ErrorResult to indicate an error.
                context.ErrorResult = new AuthenticationFailureResult(request);
            }
            else
            {
                // Authentication was attempted and succeeded. Set Principal to the authenticated user.
                context.Principal = principal;
            }
        }

        /// <summary>
        /// Only add challenge to unauthorized requests.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task ChallengeAsync(HttpAuthenticationChallengeContext context, CancellationToken cancellationToken)
        {
            var error = "invalid_token";

            // A correct implementation should verify that Realm does not contain a quote character unless properly
            // escaped (precededed by a backslash that is not itself escaped).
            var parameter = string.Format("realm=\"{0}\",error=\"{1}\"", Realm, error);
            var challenge = new AuthenticationHeaderValue(Scheme, parameter);

            context.Result = new AddChallengeOnUnauthorizedResult(challenge, context.Result);

            return Task.FromResult(0);
        }
    }
}
