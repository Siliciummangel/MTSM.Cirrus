using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Admin.Infrastructure;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Security;
using System.CommandLine;

namespace MTSM.Cirrus.Admin.Commands;

public static class ApiKeyCommand
{
    public static Command Create(AdminCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var command = new Command("api-key", "Manage API-key credentials");
        command.Subcommands.Add(CreateCreateCommand(context));
        command.Subcommands.Add(CreateListCommand(context));
        command.Subcommands.Add(CreateRotateCommand(context));
        command.Subcommands.Add(CreateRevokeCommand(context));
        return command;
    }

    private static Command CreateCreateCommand(AdminCommandContext context)
    {
        var tenantOption = MachineCommand.RequiredTenantOption();
        var machineOption = MachineCommand.RequiredMachineNameOption("--machine");
        var expiresAtOption = ExpiresAtOption();
        var descriptionOption = new Option<string?>("--description")
        {
            Description = "Purpose of this credential"
        };
        var command = new Command("create", "Create an API key");
        command.Options.Add(tenantOption);
        command.Options.Add(machineOption);
        command.Options.Add(expiresAtOption);
        command.Options.Add(descriptionOption);

        command.SetAction(async (parseResult, cancellationToken) =>
            await context.ExecuteAsync(async (db, actor, token) =>
            {
                MachineIdentity machine = await GetRequiredMachineAsync(
                    db,
                    parseResult.GetRequiredValue(tenantOption),
                    parseResult.GetRequiredValue(machineOption),
                    token);

                if (machine.Status != MachineIdentityStatus.Active)
                    throw new InvalidOperationException("Machine identity is not active.");

                GeneratedApiKey generated = ApiKeySecret.Generate();
                var credential = new ApiKeyCredential
                {
                    MachineIdentityId = machine.MachineIdentityId,
                    KeyId = generated.KeyId,
                    SecretHash = generated.SecretHash,
                    HashAlgorithm = ApiKeySecret.HashAlgorithm,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = parseResult.GetValue(expiresAtOption),
                    Description = parseResult.GetValue(descriptionOption)
                };

                db.ApiKeyCredentials.Add(credential);
                db.SecurityAuditEvents.Add(AdminAuditEvent.Create(
                    machine,
                    "ApiKeyCreated",
                    actor,
                    generated.KeyId));
                await db.SaveChangesAsync(token);

                WriteGeneratedKey("API key created", generated.Value);
                return CliExitCodes.Success;
            }, cancellationToken));

        return command;
    }

    private static Command CreateListCommand(AdminCommandContext context)
    {
        var tenantOption = MachineCommand.RequiredTenantOption();
        var machineOption = MachineCommand.RequiredMachineNameOption("--machine");
        var command = new Command("list", "List API-key metadata");
        command.Options.Add(tenantOption);
        command.Options.Add(machineOption);

        command.SetAction(async (parseResult, cancellationToken) =>
            await context.ExecuteAsync(async (db, _, token) =>
            {
                MachineIdentity machine = await GetRequiredMachineAsync(
                    db,
                    parseResult.GetRequiredValue(tenantOption),
                    parseResult.GetRequiredValue(machineOption),
                    token);

                foreach (ApiKeyCredential key in await db.ApiKeyCredentials
                    .AsNoTracking()
                    .Where(item => item.MachineIdentityId == machine.MachineIdentityId)
                    .OrderBy(item => item.CreatedAt)
                    .ToListAsync(token))
                {
                    Console.WriteLine(
                        $"{key.KeyId}\t{key.Status}\tcreated={key.CreatedAt:O}\t" +
                        $"expires={key.ExpiresAt:O}\tlastUsed={key.LastUsedAt:O}");
                }

                return CliExitCodes.Success;
            }, cancellationToken));

        return command;
    }

    private static Command CreateRotateCommand(AdminCommandContext context)
    {
        var keyIdOption = RequiredKeyIdOption();
        var expiresAtOption = ExpiresAtOption();
        var command = new Command("rotate", "Rotate and immediately revoke an API key");
        command.Options.Add(keyIdOption);
        command.Options.Add(expiresAtOption);

        command.SetAction(async (parseResult, cancellationToken) =>
            await context.ExecuteAsync(async (db, actor, token) =>
            {
                string keyId = parseResult.GetRequiredValue(keyIdOption);
                ApiKeyCredential old = await db.ApiKeyCredentials
                    .Include(item => item.MachineIdentity)
                    .SingleOrDefaultAsync(item => item.KeyId == keyId, token)
                    ?? throw new InvalidOperationException("API key does not exist.");

                if (old.Status != ApiKeyStatus.Active)
                    throw new InvalidOperationException("API key is not active.");

                DateTimeOffset now = DateTimeOffset.UtcNow;
                GeneratedApiKey generated = ApiKeySecret.Generate();
                var replacement = new ApiKeyCredential
                {
                    MachineIdentityId = old.MachineIdentityId,
                    KeyId = generated.KeyId,
                    SecretHash = generated.SecretHash,
                    HashAlgorithm = ApiKeySecret.HashAlgorithm,
                    CreatedAt = now,
                    ExpiresAt = parseResult.GetValue(expiresAtOption)
                        ?? (old.ExpiresAt > now ? old.ExpiresAt : null),
                    Description = old.Description
                };

                db.ApiKeyCredentials.Add(replacement);
                old.Status = ApiKeyStatus.Revoked;
                old.RevokedAt = now;
                old.ReplacedBy = replacement;
                db.SecurityAuditEvents.Add(AdminAuditEvent.Create(
                    old.MachineIdentity,
                    "ApiKeyRotated",
                    actor,
                    old.KeyId,
                    $"replacement:{generated.KeyId}"));
                await db.SaveChangesAsync(token);

                WriteGeneratedKey("API key rotated", generated.Value);
                return CliExitCodes.Success;
            }, cancellationToken));

        return command;
    }

    private static Command CreateRevokeCommand(AdminCommandContext context)
    {
        var keyIdOption = RequiredKeyIdOption();
        var command = new Command("revoke", "Revoke an API key");
        command.Options.Add(keyIdOption);

        command.SetAction(async (parseResult, cancellationToken) =>
            await context.ExecuteAsync(async (db, actor, token) =>
            {
                string keyId = parseResult.GetRequiredValue(keyIdOption);
                ApiKeyCredential key = await db.ApiKeyCredentials
                    .Include(item => item.MachineIdentity)
                    .SingleOrDefaultAsync(item => item.KeyId == keyId, token)
                    ?? throw new InvalidOperationException("API key does not exist.");

                if (key.Status == ApiKeyStatus.Revoked)
                    return CliExitCodes.Success;

                key.Status = ApiKeyStatus.Revoked;
                key.RevokedAt = DateTimeOffset.UtcNow;
                db.SecurityAuditEvents.Add(AdminAuditEvent.Create(
                    key.MachineIdentity,
                    "ApiKeyRevoked",
                    actor,
                    key.KeyId));
                await db.SaveChangesAsync(token);
                Console.WriteLine($"Revoked API key {keyId}.");
                return CliExitCodes.Success;
            }, cancellationToken));

        return command;
    }

    private static Option<string> RequiredKeyIdOption() => new("--key-id")
    {
        Description = "Public API-key identifier",
        Required = true
    };

    private static Option<DateTimeOffset?> ExpiresAtOption() => new("--expires-at")
    {
        Description = "Optional future expiration in ISO-8601 format",
        CustomParser = result =>
        {
            if (result.Tokens.Count == 0)
                return null;

            if (DateTimeOffset.TryParse(
                    result.Tokens.Single().Value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out DateTimeOffset value)
                && value > DateTimeOffset.UtcNow)
            {
                return value;
            }

            result.AddError("The expiration must be a future ISO-8601 timestamp.");
            return null;
        }
    };

    private static async Task<MachineIdentity> GetRequiredMachineAsync(
        MTSM.Cirrus.Core.Data.CirrusDbContext db,
        long tenantId,
        string name,
        CancellationToken cancellationToken) =>
        await db.MachineIdentities.SingleOrDefaultAsync(
            item => item.TenantId == tenantId && item.Name == name,
            cancellationToken)
        ?? throw new InvalidOperationException("Machine identity does not exist.");

    private static void WriteGeneratedKey(string message, string value)
    {
        Console.WriteLine($"{message}. Store it now; it cannot be displayed again:");
        Console.WriteLine(value);
    }
}
