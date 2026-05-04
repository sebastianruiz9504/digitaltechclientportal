using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DigitalTechClientPortal.Services
{
    public sealed class PortalPermissionService
    {
        public const string LimitedTable = "cr07a_usuariosconaccesolimitado";
        public const string LimitedEmailField = "cr07a_name";
        public const string LimitedClienteLookup = "cr07a_cliente";
        public const string LimitedModulesField = "cr07a_modulospermitidos";
        public const string LimitedActiveField = "cr07a_activo";
        public const string LimitedDisplayNameField = "cr07a_nombreusuario";

        private readonly ServiceClient _svc;

        public PortalPermissionService(ServiceClient svc)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
        }

        public async Task<PortalAccessContext> GetAccessForEmailAsync(string? email)
        {
            var normalizedEmail = NormalizeEmail(email);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return PortalAccessContext.Empty;
            }

            var principal = await TryResolvePrincipalClienteAsync(normalizedEmail);
            if (principal.Found)
            {
                return new PortalAccessContext
                {
                    IsPrincipal = true,
                    IsLimited = false,
                    IsActive = true,
                    ClienteId = principal.ClienteId,
                    ClienteNombre = principal.ClienteNombre,
                    AllowedModules = PortalModuleKeys.AllKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
                    PermissionColumnsAvailable = true
                };
            }

            var limited = await TryResolveLimitedUserAsync(normalizedEmail);
            if (limited.Found)
            {
                return new PortalAccessContext
                {
                    IsPrincipal = false,
                    IsLimited = true,
                    IsActive = limited.Active,
                    ClienteId = limited.ClienteId,
                    ClienteNombre = limited.ClienteNombre,
                    AllowedModules = limited.Modules,
                    PermissionColumnsAvailable = limited.PermissionColumnsAvailable
                };
            }

            return PortalAccessContext.Unrestricted;
        }

        public async Task<bool> CanAccessModuleAsync(string? email, string moduleKey)
        {
            if (!PortalModuleKeys.IsValid(moduleKey))
            {
                return false;
            }

            var access = await GetAccessForEmailAsync(email);
            if (access.IsPrincipal)
            {
                return true;
            }

            if (!access.IsLimited)
            {
                return true;
            }

            return access.IsActive && access.AllowedModules.Contains(moduleKey);
        }

        public async Task<(bool Found, Guid ClienteId, string? ClienteNombre)> TryResolveClienteForLimitedAsync(string email)
        {
            var limited = await TryResolveLimitedUserAsync(email);
            if (!limited.Found || !limited.Active)
            {
                return (false, Guid.Empty, null);
            }

            return (true, limited.ClienteId, limited.ClienteNombre);
        }

        public async Task<(bool Found, Guid ClienteId, string? ClienteNombre)> TryResolvePrincipalClienteAsync(string? email)
        {
            var normalizedEmail = NormalizeEmail(email);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return (false, Guid.Empty, null);
            }

            var q = new QueryExpression("cr07a_cliente")
            {
                ColumnSet = new ColumnSet("cr07a_clienteid", "cr07a_nombre", "cr07a_name"),
                TopCount = 1,
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("cr07a_correoelectronico", ConditionOperator.Equal, normalizedEmail)
                    }
                }
            };

            var result = await _svc.RetrieveMultipleAsync(q);
            var entity = result.Entities.FirstOrDefault();
            if (entity == null)
            {
                return (false, Guid.Empty, null);
            }

            var nombre = entity.GetAttributeValue<string>("cr07a_nombre")
                         ?? entity.GetAttributeValue<string>("cr07a_name");

            return (true, entity.Id, nombre);
        }

        public async Task<PermissionUserListResult> GetUsersForPrincipalAsync(string? principalEmail)
        {
            var principal = await TryResolvePrincipalClienteAsync(principalEmail);
            if (!principal.Found || principal.ClienteId == Guid.Empty)
            {
                return PermissionUserListResult.Forbidden;
            }

            var result = await RetrieveLimitedUsersForClienteAsync(principal.ClienteId);
            var users = result.Rows
                .Select(row => MapPermissionUser(row, result.PermissionColumnsAvailable))
                .OrderBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new PermissionUserListResult
            {
                IsPrincipal = true,
                ClienteId = principal.ClienteId,
                ClienteNombre = principal.ClienteNombre,
                PermissionColumnsAvailable = result.PermissionColumnsAvailable,
                Users = users
            };
        }

        public async Task UpsertUserPermissionAsync(string? principalEmail, PermissionUserRecord input)
        {
            var principal = await TryResolvePrincipalClienteAsync(principalEmail);
            if (!principal.Found || principal.ClienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("Solo el usuario principal del cliente puede administrar permisos.");
            }

            var email = NormalizeEmail(input.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("Debes ingresar el correo del usuario.");
            }

            if (string.Equals(NormalizeEmail(principalEmail), email, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("El usuario principal ya tiene acceso completo y no necesita permisos adicionales.");
            }

            var modules = input.Modules
                .Where(PortalModuleKeys.IsValid)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (modules.Count == 0)
            {
                throw new InvalidOperationException("Selecciona al menos una pagina para este usuario.");
            }

            Entity entity;
            if (input.Id != Guid.Empty)
            {
                var existing = await RetrieveLimitedUserByIdAsync(input.Id);
                EnsureBelongsToCliente(existing, principal.ClienteId);
                entity = new Entity(LimitedTable, input.Id);
            }
            else
            {
                var existing = await RetrieveLimitedUserByEmailAndClienteAsync(email, principal.ClienteId);
                entity = existing == null
                    ? new Entity(LimitedTable)
                    : new Entity(LimitedTable, existing.Id);
            }

            entity[LimitedEmailField] = email;
            entity[LimitedClienteLookup] = new EntityReference("cr07a_cliente", principal.ClienteId);
            entity[LimitedModulesField] = string.Join(";", modules);
            entity[LimitedActiveField] = input.Active;
            var displayName = input.DisplayName?.Trim();
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                entity[LimitedDisplayNameField] = displayName;
            }

            await SavePermissionEntityWithFallbacksAsync(entity);
        }

        public async Task DeleteUserPermissionAsync(string? principalEmail, Guid id)
        {
            if (id == Guid.Empty)
            {
                return;
            }

            var principal = await TryResolvePrincipalClienteAsync(principalEmail);
            if (!principal.Found || principal.ClienteId == Guid.Empty)
            {
                throw new UnauthorizedAccessException("Solo el usuario principal del cliente puede administrar permisos.");
            }

            var existing = await RetrieveLimitedUserByIdAsync(id);
            EnsureBelongsToCliente(existing, principal.ClienteId);
            await _svc.DeleteAsync(LimitedTable, id);
        }

        public static string NormalizeEmail(string? email)
        {
            return (email ?? string.Empty).Trim().ToLowerInvariant();
        }

        public static IReadOnlySet<string> ParseModules(string? raw, bool permissionColumnsAvailable)
        {
            if (!permissionColumnsAvailable)
            {
                return PortalModuleKeys.AllKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return PortalModuleKeys.AllKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            return raw
                .Split(new[] { ';', ',', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(PortalModuleKeys.IsValid)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public static string BuildMissingColumnsMessage()
        {
            return "Faltan columnas en Dataverse para administrar permisos. En la tabla cr07a_usuariosconaccesolimitado crea cr07a_modulospermitidos (texto). La columna cr07a_activo es opcional y debe ser Si/No si quieres desactivar usuarios.";
        }

        private async Task<(bool Found, Guid ClienteId, string? ClienteNombre, bool Active, IReadOnlySet<string> Modules, bool PermissionColumnsAvailable)> TryResolveLimitedUserAsync(string? email)
        {
            var normalizedEmail = NormalizeEmail(email);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return (false, Guid.Empty, null, false, new HashSet<string>(), true);
            }

            var result = await RetrieveLimitedUserByEmailAsync(normalizedEmail);
            if (result.Row == null)
            {
                return (false, Guid.Empty, null, false, new HashSet<string>(), result.PermissionColumnsAvailable);
            }

            var clienteRef = result.Row.GetAttributeValue<EntityReference>(LimitedClienteLookup);
            if (clienteRef == null || clienteRef.Id == Guid.Empty)
            {
                return (false, Guid.Empty, null, false, new HashSet<string>(), result.PermissionColumnsAvailable);
            }

            var active = GetActiveValue(result.Row, result.PermissionColumnsAvailable);

            var modules = ParseModules(
                result.Row.GetAttributeValue<string>(LimitedModulesField),
                result.PermissionColumnsAvailable);

            var nombre = await ResolveClienteNombreAsync(clienteRef);
            return (true, clienteRef.Id, nombre, active, modules, result.PermissionColumnsAvailable);
        }

        private async Task<(Entity? Row, bool PermissionColumnsAvailable)> RetrieveLimitedUserByEmailAsync(string email)
        {
            var q = CreateLimitedUserBaseQuery(includePermissionColumns: true);
            q.TopCount = 1;
            q.Criteria.AddCondition(LimitedEmailField, ConditionOperator.Equal, email);

            try
            {
                var result = await _svc.RetrieveMultipleAsync(q);
                return (result.Entities.FirstOrDefault(), true);
            }
            catch (Exception ex) when (IsMissingPermissionColumnException(ex))
            {
                q = CreateLimitedUserBaseQuery(includePermissionColumns: false);
                q.TopCount = 1;
                q.Criteria.AddCondition(LimitedEmailField, ConditionOperator.Equal, email);
                var result = await _svc.RetrieveMultipleAsync(q);
                return (result.Entities.FirstOrDefault(), false);
            }
        }

        private async Task<Entity?> RetrieveLimitedUserByEmailAndClienteAsync(string email, Guid clienteId)
        {
            var q = CreateLimitedUserBaseQuery(includePermissionColumns: false);
            q.TopCount = 1;
            q.Criteria.AddCondition(LimitedEmailField, ConditionOperator.Equal, email);
            q.Criteria.AddCondition(LimitedClienteLookup, ConditionOperator.Equal, clienteId);

            var result = await _svc.RetrieveMultipleAsync(q);
            return result.Entities.FirstOrDefault();
        }

        private async Task<Entity> RetrieveLimitedUserByIdAsync(Guid id)
        {
            try
            {
                return await _svc.RetrieveAsync(
                    LimitedTable,
                    id,
                    new ColumnSet(LimitedEmailField, LimitedClienteLookup, LimitedModulesField, LimitedActiveField));
            }
            catch (Exception ex) when (IsMissingPermissionColumnException(ex))
            {
                return await _svc.RetrieveAsync(
                    LimitedTable,
                    id,
                    new ColumnSet(LimitedEmailField, LimitedClienteLookup));
            }
        }

        private async Task<(IReadOnlyList<Entity> Rows, bool PermissionColumnsAvailable)> RetrieveLimitedUsersForClienteAsync(Guid clienteId)
        {
            var q = CreateLimitedUserBaseQuery(includePermissionColumns: true);
            q.Criteria.AddCondition(LimitedClienteLookup, ConditionOperator.Equal, clienteId);
            q.AddOrder(LimitedEmailField, OrderType.Ascending);

            try
            {
                var result = await _svc.RetrieveMultipleAsync(q);
                return (result.Entities.ToList(), true);
            }
            catch (Exception ex) when (IsMissingPermissionColumnException(ex))
            {
                q = CreateLimitedUserBaseQuery(includePermissionColumns: false);
                q.Criteria.AddCondition(LimitedClienteLookup, ConditionOperator.Equal, clienteId);
                q.AddOrder(LimitedEmailField, OrderType.Ascending);
                var result = await _svc.RetrieveMultipleAsync(q);
                return (result.Entities.ToList(), false);
            }
        }

        private static QueryExpression CreateLimitedUserBaseQuery(bool includePermissionColumns)
        {
            var columns = includePermissionColumns
                ? new ColumnSet(LimitedEmailField, LimitedClienteLookup, LimitedModulesField, LimitedActiveField)
                : new ColumnSet(LimitedEmailField, LimitedClienteLookup);

            return new QueryExpression(LimitedTable)
            {
                ColumnSet = columns
            };
        }

        private static PermissionUserRecord MapPermissionUser(Entity row, bool permissionColumnsAvailable)
        {
            var active = GetActiveValue(row, permissionColumnsAvailable);

            return new PermissionUserRecord
            {
                Id = row.Id,
                Email = row.GetAttributeValue<string>(LimitedEmailField) ?? string.Empty,
                DisplayName = row.GetAttributeValue<string>(LimitedDisplayNameField) ?? string.Empty,
                Active = active,
                Modules = ParseModules(row.GetAttributeValue<string>(LimitedModulesField), permissionColumnsAvailable).ToList()
            };
        }

        private async Task<string?> ResolveClienteNombreAsync(EntityReference clienteRef)
        {
            if (!string.IsNullOrWhiteSpace(clienteRef.Name))
            {
                return clienteRef.Name;
            }

            try
            {
                var cliente = await _svc.RetrieveAsync("cr07a_cliente", clienteRef.Id, new ColumnSet("cr07a_name", "cr07a_nombre"));
                return cliente.GetAttributeValue<string>("cr07a_nombre")
                       ?? cliente.GetAttributeValue<string>("cr07a_name");
            }
            catch
            {
                return null;
            }
        }

        private async Task SavePermissionEntityAsync(Entity entity)
        {
            if (entity.Id == Guid.Empty)
            {
                await _svc.CreateAsync(entity);
            }
            else
            {
                await _svc.UpdateAsync(entity);
            }
        }

        private async Task SavePermissionEntityWithFallbacksAsync(Entity entity)
        {
            try
            {
                await SavePermissionEntityAsync(entity);
            }
            catch (Exception ex) when (entity.Attributes.ContainsKey(LimitedDisplayNameField) && IsMissingDisplayNameColumnException(ex))
            {
                entity.Attributes.Remove(LimitedDisplayNameField);
                await SavePermissionEntityWithFallbacksAsync(entity);
            }
            catch (Exception ex) when (entity.Attributes.ContainsKey(LimitedActiveField) && IsActiveColumnNotBooleanException(ex))
            {
                entity.Attributes.Remove(LimitedActiveField);
                await SavePermissionEntityWithFallbacksAsync(entity);
            }
            catch (Exception ex) when (IsMissingPermissionColumnException(ex))
            {
                throw new InvalidOperationException(BuildMissingColumnsMessage(), ex);
            }
        }

        private static void EnsureBelongsToCliente(Entity entity, Guid clienteId)
        {
            var clienteRef = entity.GetAttributeValue<EntityReference>(LimitedClienteLookup);
            if (clienteRef == null || clienteRef.Id != clienteId)
            {
                throw new UnauthorizedAccessException("No puedes modificar permisos de otro cliente.");
            }
        }

        private static bool GetActiveValue(Entity row, bool permissionColumnsAvailable)
        {
            if (!permissionColumnsAvailable || !row.Attributes.TryGetValue(LimitedActiveField, out var raw) || raw == null)
            {
                return true;
            }

            return raw switch
            {
                bool value => value,
                OptionSetValue option => option.Value != 0,
                int value => value != 0,
                string value when bool.TryParse(value, out var parsed) => parsed,
                string value when int.TryParse(value, out var parsed) => parsed != 0,
                _ => true
            };
        }

        private static bool IsMissingPermissionColumnException(Exception ex)
        {
            var current = ex;
            while (current != null)
            {
                var message = current.Message ?? string.Empty;
                if ((message.Contains(LimitedModulesField, StringComparison.OrdinalIgnoreCase) ||
                     message.Contains(LimitedActiveField, StringComparison.OrdinalIgnoreCase)) &&
                    (message.Contains("Could not find", StringComparison.OrdinalIgnoreCase) ||
                     message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                     message.Contains("not found", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        private static bool IsMissingDisplayNameColumnException(Exception ex)
        {
            var current = ex;
            while (current != null)
            {
                var message = current.Message ?? string.Empty;
                if (message.Contains(LimitedDisplayNameField, StringComparison.OrdinalIgnoreCase) &&
                    (message.Contains("Could not find", StringComparison.OrdinalIgnoreCase) ||
                     message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                     message.Contains("not found", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        private static bool IsActiveColumnNotBooleanException(Exception ex)
        {
            var current = ex;
            while (current != null)
            {
                var message = current.Message ?? string.Empty;
                if (message.Contains(LimitedActiveField, StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("System.Boolean", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("Boolean", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("Incorrect attribute value type", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("invalid attribute value type", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current = current.InnerException;
            }

            return false;
        }
    }

    public sealed class PortalAccessContext
    {
        public static PortalAccessContext Empty { get; } = new();

        public static PortalAccessContext Unrestricted { get; } = new()
        {
            IsActive = true,
            AllowedModules = PortalModuleKeys.AllKeys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            PermissionColumnsAvailable = true
        };

        public bool IsPrincipal { get; init; }
        public bool IsLimited { get; init; }
        public bool IsActive { get; init; }
        public Guid ClienteId { get; init; }
        public string? ClienteNombre { get; init; }
        public IReadOnlySet<string> AllowedModules { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool PermissionColumnsAvailable { get; init; } = true;
    }

    public sealed class PermissionUserListResult
    {
        public static PermissionUserListResult Forbidden { get; } = new();

        public bool IsPrincipal { get; init; }
        public Guid ClienteId { get; init; }
        public string? ClienteNombre { get; init; }
        public bool PermissionColumnsAvailable { get; init; } = true;
        public List<PermissionUserRecord> Users { get; init; } = new();
    }

    public sealed class PermissionUserRecord
    {
        public Guid Id { get; init; }
        public string Email { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool Active { get; init; } = true;
        public List<string> Modules { get; init; } = new();
    }
}
