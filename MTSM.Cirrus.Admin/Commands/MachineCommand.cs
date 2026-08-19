using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Admin.Infrastructure;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Security;
using System.CommandLine;

namespace MTSM.Cirrus.Admin.Commands;

public static class MachineCommand
{
    public static Command Create(AdminCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var command = new Command(
            "machine",
            "Manage tenant-bound machine identities");

        command.Subcommands.Add(CreateCreateCommand(context));
        command.Subcommands.Add(CreateListCommand(context));
        command.Subcommands.Add(CreateDisableCommand(context));
        command.Subcommands.Add(CreatePermissionCommand(context, grant: true));
        command.Subcommands.Add(CreatePermissionCommand(context, grant: false));

        return command;
    }

    private static Command CreateCreateCommand(AdminCommandContext context)
    {
        var tenantOption = RequiredTenantOption();
        var nameOption = RequiredMachineNameOption("--name");
        var permissionsOption = new Option<string[]>("--permission")
        {
            Description = "Permission to grant; may be specified multiple times",
            Required = true,
            AllowMultipleArgumentsPerToken = true
        };
        permissionsOption.Validators.Add(result =>
        {
            foreach (string value in result.Tokens.Select(token => token.Value))
            {
                if (!IsKnownPermission(value))
                    result.AddError($"Unknown permission '{value}'.");
            }
        });
        var displayNameOption = new Option<string?>("--display-name")
        {
            Description = "Human-readable display name"
        };
        var descriptionOption = new Option<string?>("--description")
        {
            Description = "Purpose of the machine identity"
        };

        var command = new Command("create", "Create a machine identity");
        command.Options.Add(tenantOption);
        command.Options.Add(nameOption);
        command.Options.Add(permissionsOption);
        command.Options.Add(displayNameOption);
        command.Options.Add(descriptionOption);

        command.SetAction(async (parseResult, cancellationToken) =>
            await context.ExecuteAsync(async (db, actor, token) =>
            {
                long tenantId = parseResult.GetRequiredValue(tenantOption);
                string name = parseResult.GetRequiredValue(nameOption);
                string[] permissionNames = parseResult.GetRequiredValue(permissionsOption);

                Tenant tenant = await db.Tenants.SingleOrDefaultAsync(
                    item => item.TenantId == tenantId,
                    token) ?? throw new InvalidOperationException(
                        $"Tenant {tenantId} does not exist.");

                if (tenant.Status != TenantStatus.Active)
                    throw new InvalidOperationException("Tenant is not active.");

                var machine = new MachineIdentity
                {
                    TenantId = tenantId,
                    Name = name,
                    DisplayName = parseResult.GetValue(displayNameOption),
                    Description = parseResult.GetValue(descriptionOption),
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = actor
                };

                foreach (CirrusPermission permission in permissionNames
                    .Select(ParsePermission)
                    .Distinct())
                {
                    machine.Permissions.Add(new MachineIdentityPermission
                    {
                        Permission = permission
                    });
                }

                db.MachineIdentities.Add(machine);
                db.SecurityAuditEvents.Add(AdminAuditEvent.Create(
                    machine,
                    "MachineIdentityCreated",
                    actor));

                await db.SaveChangesAsync(token);

                Console.WriteLine(
                    $"Created machine identity {machine.MachineIdentityId} " +
                    $"({machine.Name}) for tenant {tenantId}.");

                return CliExitCodes.Success;
            }, cancellationToken));

        return command;
    }

    private static Command CreateListCommand(AdminCommandContext context)
    {
        var tenantOption = new Option<long?>("--tenant")
        {
            Description = "Optional tenant filter"
        };
        var command = new Command("list", "List machine identities");
        command.Options.Add(tenantOption);

        command.SetAction(async (parseResult, cancellationToken) =>
            await context.ExecuteAsync(async (db, _, token) =>
            {
                long? tenantId = parseResult.GetValue(tenantOption);
                IQueryable<MachineIdentity> query = db.MachineIdentities
                    .AsNoTracking()
                    .Include(item => item.Permissions);

                if (tenantId.HasValue)
                    query = query.Where(item => item.TenantId == tenantId);

                foreach (MachineIdentity item in await query
                    .OrderBy(item => item.TenantId)
                    .ThenBy(item => item.Name)
                    .ToListAsync(token))
                {
                    Console.WriteLine(
                        $"{item.MachineIdentityId}\t{item.TenantId}\t" +
                        $"{item.Name}\t{item.Status}\t" +
                        string.Join(',', item.Permissions.Select(
                            permission => permission.Permission.ToExternalName())));
                }

                return CliExitCodes.Success;
            }, cancellationToken));

        return command;
    }

    private static Command CreateDisableCommand(AdminCommandContext context)
    {
        var tenantOption = RequiredTenantOption();
        var machineOption = RequiredMachineNameOption("--machine");
        var command = new Command("disable", "Disable a machine identity");
        command.Options.Add(tenantOption);
        command.Options.Add(machineOption);

        command.SetAction(async (parseResult, cancellationToken) =>
            await context.ExecuteAsync(async (db, actor, token) =>
            {
                MachineIdentity machine = await GetRequiredMachineAsync(
                    db,
                    parseResult.GetRequiredValue(tenantOption),
                    parseResult.GetRequiredValue(machineOption),
                    token);

                if (machine.Status == MachineIdentityStatus.Disabled)
                    return CliExitCodes.Success;

                machine.Status = MachineIdentityStatus.Disabled;
                machine.DisabledAt = machine.UpdatedAt = DateTimeOffset.UtcNow;
                db.SecurityAuditEvents.Add(AdminAuditEvent.Create(
                    machine,
                    "MachineIdentityDisabled",
                    actor));
                await db.SaveChangesAsync(token);
                Console.WriteLine($"Disabled machine identity {machine.MachineIdentityId}.");
                return CliExitCodes.Success;
            }, cancellationToken));

        return command;
    }

    private static Command CreatePermissionCommand(
        AdminCommandContext context,
        bool grant)
    {
        var tenantOption = RequiredTenantOption();
        var machineOption = RequiredMachineNameOption("--machine");
        var permissionOption = new Option<string>("--permission")
        {
            Description = "Cirrus permission",
            Required = true
        };
        permissionOption.Validators.Add(result =>
        {
            if (result.Tokens.Count == 1
                && !IsKnownPermission(result.Tokens[0].Value))
                result.AddError($"Unknown permission '{result.Tokens[0].Value}'.");
        });
        string commandName = grant ? "grant" : "revoke";
        var command = new Command(commandName, $"{commandName} a machine permission");
        command.Options.Add(tenantOption);
        command.Options.Add(machineOption);
        command.Options.Add(permissionOption);

        command.SetAction(async (parseResult, cancellationToken) =>
            await context.ExecuteAsync(async (db, actor, token) =>
            {
                MachineIdentity machine = await db.MachineIdentities
                    .Include(item => item.Permissions)
                    .SingleOrDefaultAsync(item =>
                        item.TenantId == parseResult.GetRequiredValue(tenantOption)
                        && item.Name == parseResult.GetRequiredValue(machineOption), token)
                    ?? throw new InvalidOperationException("Machine identity does not exist.");

                CirrusPermission permission = ParsePermission(
                    parseResult.GetRequiredValue(permissionOption));
                MachineIdentityPermission? existing = machine.Permissions
                    .SingleOrDefault(item => item.Permission == permission);

                if (grant && existing is null)
                    machine.Permissions.Add(new() { Permission = permission });
                else if (!grant && existing is not null)
                    db.MachineIdentityPermissions.Remove(existing);

                machine.UpdatedAt = DateTimeOffset.UtcNow;
                db.SecurityAuditEvents.Add(AdminAuditEvent.Create(
                    machine,
                    "PermissionsChanged",
                    actor,
                    details: $"{commandName}:{permission.ToExternalName()}"));
                await db.SaveChangesAsync(token);
                return CliExitCodes.Success;
            }, cancellationToken));

        return command;
    }

    internal static Option<long> RequiredTenantOption() => new("--tenant")
    {
        Description = "Positive Cirrus tenant ID",
        Required = true,
        CustomParser = result =>
        {
            if (long.TryParse(result.Tokens.Single().Value, out long value) && value > 0)
                return value;
            result.AddError("The tenant ID must be greater than zero.");
            return 0;
        }
    };

    internal static Option<string> RequiredMachineNameOption(string name) => new(name)
    {
        Description = "Machine identity name",
        Required = true
    };

    internal static CirrusPermission ParsePermission(string value) => value switch
    {
        CirrusPermissionNames.ArchiveRead => CirrusPermission.ArchiveRead,
        CirrusPermissionNames.ArchiveWrite => CirrusPermission.ArchiveWrite,
        CirrusPermissionNames.ArchiveDelete => CirrusPermission.ArchiveDelete,
        CirrusPermissionNames.ArchiveVerify => CirrusPermission.ArchiveVerify,
        _ => throw new ArgumentException($"Unknown permission '{value}'.")
    };

    private static bool IsKnownPermission(string value) => value is
        CirrusPermissionNames.ArchiveRead
        or CirrusPermissionNames.ArchiveWrite
        or CirrusPermissionNames.ArchiveDelete
        or CirrusPermissionNames.ArchiveVerify;

    private static async Task<MachineIdentity> GetRequiredMachineAsync(
        MTSM.Cirrus.Core.Data.CirrusDbContext db,
        long tenantId,
        string name,
        CancellationToken cancellationToken) =>
        await db.MachineIdentities.SingleOrDefaultAsync(
            item => item.TenantId == tenantId && item.Name == name,
            cancellationToken)
        ?? throw new InvalidOperationException("Machine identity does not exist.");
}
