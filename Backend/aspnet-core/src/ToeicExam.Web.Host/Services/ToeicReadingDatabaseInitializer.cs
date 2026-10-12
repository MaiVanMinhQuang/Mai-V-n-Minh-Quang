using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace ToeicExam.Web.Host.Services
{
    public class ToeicReadingDatabaseInitializer
    {
        private readonly IConfiguration _configuration;

        public ToeicReadingDatabaseInitializer(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task InitializeAsync()
        {
            var connectionString = _configuration.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:Default must be configured.");
            }

            var builder = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            {
                throw new InvalidOperationException("ConnectionStrings:Default must include Database=toeic_reading (or the configured database name).");
            }

            var masterBuilder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
            using (var master = new SqlConnection(masterBuilder.ConnectionString))
            {
                await master.OpenAsync();
                using (var command = master.CreateCommand())
                {
                    command.CommandText = @"IF DB_ID(@database) IS NULL
BEGIN
    DECLARE @createDatabase nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@database);
    EXEC sys.sp_executesql @createDatabase;
END";
                    command.Parameters.AddWithValue("@database", builder.InitialCatalog);
                    await command.ExecuteNonQueryAsync();
                }
            }

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var assembly = typeof(ToeicReadingDatabaseInitializer).GetTypeInfo().Assembly;
                var resource = "ToeicExam.Web.Host.Sql.toeic_reading.sql";
                using (var stream = assembly.GetManifestResourceStream(resource))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException("The TOEIC database schema resource was not embedded.");
                    }

                    using (var reader = new StreamReader(stream))
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = await reader.ReadToEndAsync();
                        await command.ExecuteNonQueryAsync();
                    }
                }

                await SeedUserAsync(connection, "Quản trị viên", "admin@toeic.local", "Admin123!", "admin");
            }
        }

        private static async Task SeedUserAsync(SqlConnection connection, string name, string email, string password, string role)
        {
            using (var check = connection.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(1) FROM dbo.users WHERE email = @email";
                check.Parameters.AddWithValue("@email", email);
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) != 0)
                {
                    return;
                }
            }

            var hasher = new PasswordHasher<string>();
            var passwordHash = hasher.HashPassword(email, password);
            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = @"INSERT INTO dbo.users (full_name, email, password_hash, role, is_active)
VALUES (@name, @email, @hash, @role, 1)";
                insert.Parameters.AddWithValue("@name", name);
                insert.Parameters.AddWithValue("@email", email);
                insert.Parameters.AddWithValue("@hash", passwordHash);
                insert.Parameters.AddWithValue("@role", role);
                await insert.ExecuteNonQueryAsync();
            }
        }
    }
}
