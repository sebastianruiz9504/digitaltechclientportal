using System;
using System.Linq;
using System.Threading.Tasks;
using DigitalTechClientPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalTechClientPortal.Security
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class RequireModuleAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly string _moduleKey;

        public RequireModuleAttribute(string moduleKey)
        {
            _moduleKey = moduleKey;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context.Filters.Any(f => f is IAllowAnonymousFilter))
            {
                return;
            }

            var user = context.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                context.Result = new ChallengeResult();
                return;
            }

            var emails = UserEmailResolver.GetCandidateEmails(user);
            var permissions = context.HttpContext.RequestServices.GetRequiredService<PortalPermissionService>();
            bool canAccess;
            try
            {
                canAccess = await permissions.CanAccessModuleAsync(emails, _moduleKey);
            }
            catch
            {
                canAccess = false;
            }

            if (!canAccess)
            {
                context.Result = new RedirectToActionResult(
                    "Denegado",
                    "Permisos",
                    new { modulo = _moduleKey });
            }
        }
    }
}
