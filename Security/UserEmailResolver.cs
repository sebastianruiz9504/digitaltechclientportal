using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace DigitalTechClientPortal.Security
{
    public static class UserEmailResolver
    {
        private static readonly string[] PreferredClaimTypes =
        {
            "preferred_username",
            "upn",
            "email",
            "emails",
            ClaimTypes.Upn,
            ClaimTypes.Email
        };

        public static string? GetCurrentEmail(ClaimsPrincipal? user)
        {
            return GetCandidateEmails(user).FirstOrDefault();
        }

        public static IReadOnlyList<string> GetCandidateEmails(ClaimsPrincipal? user)
        {
            if (user == null)
            {
                return Array.Empty<string>();
            }

            var values = new List<string>();
            foreach (var claimType in PreferredClaimTypes)
            {
                values.AddRange(user.FindAll(claimType).Select(c => c.Value));
            }

            if (!string.IsNullOrWhiteSpace(user.Identity?.Name))
            {
                values.Add(user.Identity.Name);
            }

            return values
                .SelectMany(SplitPossibleEmailValues)
                .SelectMany(ExpandExternalUserEmail)
                .Select(NormalizeEmail)
                .Where(IsLikelyEmail)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(email => email.Contains("#ext#", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(email => email.Length)
                .ToList();
        }

        private static IEnumerable<string> SplitPossibleEmailValues(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }

            foreach (var part in value.Split(new[] { ';', ',', '|', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return part;
            }
        }

        private static IEnumerable<string> ExpandExternalUserEmail(string value)
        {
            yield return value;

            var markerIndex = value.IndexOf("#EXT#", StringComparison.OrdinalIgnoreCase);
            if (markerIndex <= 0)
            {
                yield break;
            }

            var originalPart = value[..markerIndex];
            var separatorIndex = originalPart.LastIndexOf('_');
            if (separatorIndex <= 0 || separatorIndex >= originalPart.Length - 1)
            {
                yield break;
            }

            yield return originalPart[..separatorIndex] + "@" + originalPart[(separatorIndex + 1)..];
        }

        private static string NormalizeEmail(string? email)
        {
            return (email ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static bool IsLikelyEmail(string value)
        {
            return value.Contains('@') && value.IndexOf('@') > 0 && value.IndexOf('@') < value.Length - 1;
        }
    }
}
