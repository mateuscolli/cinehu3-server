using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Api.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Jellyfin.Server.Filters
{
    /// <summary>
    /// Security requirement operation filter.
    /// </summary>
    public class SecurityRequirementsOperationFilter : IOperationFilter
    {
        /// <inheritdoc />
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var requiredScopes = new List<string>();

            var requiresAuth = false;
            // Add all method scopes.
            foreach (var attribute in context.MethodInfo.GetCustomAttributes(true))
            {
                if (attribute is not AuthorizeAttribute authorizeAttribute)
                {
                    continue;
                }

                requiresAuth = true;
                if (authorizeAttribute.Policy is not null
                    && !requiredScopes.Contains(authorizeAttribute.Policy, StringComparer.Ordinal))
                {
                    requiredScopes.Add(authorizeAttribute.Policy);
                }
            }

            // Add controller scopes if any.
            var controllerAttributes = context.MethodInfo.DeclaringType?.GetCustomAttributes(true);
            if (controllerAttributes is not null)
            {
                foreach (var attribute in controllerAttributes)
                {
                    if (attribute is not AuthorizeAttribute authorizeAttribute)
                    {
                        continue;
                    }

                    requiresAuth = true;
                    if (authorizeAttribute.Policy is not null
                        && !requiredScopes.Contains(authorizeAttribute.Policy, StringComparer.Ordinal))
                    {
                        requiredScopes.Add(authorizeAttribute.Policy);
                    }
                }
            }

            if (!requiresAuth)
            {
                return;
            }

            if (!operation.Responses.ContainsKey("401"))
            {
                operation.Responses.Add("401", new OpenApiResponse { Description = "Unauthorized" });
            }

            if (!operation.Responses.ContainsKey("403"))
            {
                operation.Responses.Add("403", new OpenApiResponse { Description = "Forbidden" });
            }

            var scheme = new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = AuthenticationSchemes.CustomAuthentication
                }
            };

            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new OpenApiSecurityRequirement
                {
                    [scheme] = requiredScopes
                }
            };
        }
    }
}
