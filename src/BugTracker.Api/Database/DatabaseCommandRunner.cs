using BugTracker.Api.Auth;

namespace BugTracker.Api.Database;

public static class DatabaseCommandRunner
{
    public static async Task<int?> RunIfRequestedAsync(
        string[] args,
        SqliteConnectionFactory connectionFactory,
        PasswordHasherService passwordHasher,
        CancellationToken ct = default)
    {
        if (args.Length == 0 || args[0] is not ("migrate" or "bootstrap-admin" or "seed-demo"))
        {
            return null;
        }

        var provisioner = new DatabaseProvisioner(connectionFactory, passwordHasher);
        try
        {
            if (args[0] == "migrate")
            {
                Console.WriteLine("Database migrations are current.");
                return 0;
            }

            if (args[0] == "seed-demo")
            {
                await provisioner.SeedDemoAsync(ct);
                Console.WriteLine("Demo data created. Credentials are documented in demo.md.");
                return 0;
            }

            var email = ReadOption(args, "--email")
                ?? throw new ArgumentException("bootstrap-admin requires --email <address>.");
            var password = Environment.GetEnvironmentVariable("BUG_TRACKER_BOOTSTRAP_PASSWORD");
            if (string.IsNullOrEmpty(password))
            {
                password = ReadSecret("Administrator password: ");
            }

            var userId = await provisioner.BootstrapAdminAsync(email, password, ct);
            Console.WriteLine($"Administrator created with user ID {userId}.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string ReadSecret(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "Set BUG_TRACKER_BOOTSTRAP_PASSWORD when bootstrap input is redirected.");
        }

        Console.Write(prompt);
        var value = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string([.. value]);
            }

            if (key.Key == ConsoleKey.Backspace && value.Count > 0)
            {
                value.RemoveAt(value.Count - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                value.Add(key.KeyChar);
            }
        }
    }
}
