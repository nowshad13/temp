using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using Microsoft.Data.SqlClient;      // For SQL Server
using MySql.Data.MySqlClient;        // For MySQL
using Npgsql;                        // For PostgreSQL
using Oracle.ManagedDataAccess.Client; // For Oracle
using System.Text.Json;
using System.Collections.Generic;
using DbConnectionTester.Models;
using System.Text.RegularExpressions;

namespace DbConnectionTester.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index() => View();

        [HttpPost]
        public IActionResult TestDbConnection(string dbType, string serverName, string dbName, string username, string password)
        {
            string connectionString = GetConnectionString(dbType, serverName, dbName, username, password);

            if (string.IsNullOrEmpty(connectionString))
            {
                ViewBag.Message = "Unsupported database type.";
                ViewBag.Success = false;
                return View("Index");
            }

            try
            {
                using (IDbConnection connection = GetDbConnection(dbType, connectionString))
                {
                    connection.Open();
                    ViewBag.Message = $"{dbType} database connection successful!";
                    ViewBag.Success = true;

                    ViewBag.DbType = dbType;
                    ViewBag.ServerName = serverName;
                    ViewBag.DbName = dbName;
                    ViewBag.Username = username;
                    ViewBag.Password = password;
                }
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"Connection failed: {ex.Message}";
                ViewBag.Success = false;
            }

            return View("Index");
        }

        [HttpPost]
        public IActionResult AddData(string dbType, string serverName, string dbName, string username, string password)
        {
            var connInfo = new Dictionary<string, string>
            {
                ["dbType"] = dbType ?? "",
                ["serverName"] = serverName ?? "",
                ["dbName"] = dbName ?? "",
                ["username"] = username ?? "",
                ["password"] = password ?? ""
            };
            TempData["ConnInfo"] = JsonSerializer.Serialize(connInfo);

            var model = new CreateTableModel
            {
                DatabaseName = dbName ?? "",
                TableName = ""
            };

            return View("CreateData", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateData(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                ModelState.AddModelError("", "No data provided.");
                return View("CreateData", new CreateTableModel());
            }

            CreateTableModel createModel;
            try
            {
                createModel = JsonSerializer.Deserialize<CreateTableModel>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Invalid payload: " + ex.Message);
                return View("CreateData", new CreateTableModel());
            }

            if (TempData["ConnInfo"] == null)
            {
                ModelState.AddModelError("", "Connection information expired. Please re-connect from the Index page.");
                return View("CreateData", createModel);
            }

            var connInfo = JsonSerializer.Deserialize<Dictionary<string, string>>(TempData["ConnInfo"].ToString());
            string dbType = connInfo.GetValueOrDefault("dbType", "");
            string server = connInfo.GetValueOrDefault("serverName", "");
            string username = connInfo.GetValueOrDefault("username", "");
            string password = connInfo.GetValueOrDefault("password", "");
            string originalDbName = connInfo.GetValueOrDefault("dbName", "");

            if (string.IsNullOrWhiteSpace(createModel.TableName) || createModel.Columns.Count == 0)
            {
                ModelState.AddModelError("", "Table name and at least one column are required.");
                return View("CreateData", createModel);
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(createModel.DatabaseName) &&
                    !string.Equals(createModel.DatabaseName, originalDbName, StringComparison.OrdinalIgnoreCase))
                {
                    if (dbType.ToLower() == "oracle")
                    {
                        ViewBag.Warning = "Oracle DB creation usually requires DBA privileges. Using existing schema.";
                    }
                    else
                    {
                        CreateDatabaseIfNotExists(dbType, server, createModel.DatabaseName, username, password);
                    }
                }

                string targetConnectionString = GetConnectionString(dbType, server, createModel.DatabaseName, username, password);
                string createTableSql = BuildCreateTableSql(dbType, createModel.TableName, createModel.Columns);

                using (IDbConnection conn = GetDbConnection(dbType, targetConnectionString))
                {
                    conn.Open();

                    using (IDbCommand cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = createTableSql;
                        cmd.ExecuteNonQuery();
                    }

                    // Insert rows
                    if (createModel.Rows != null && createModel.Rows.Count > 0)
                    {
                        foreach (var row in createModel.Rows)
                        {
                            string insertSql = BuildInsertSql(dbType, createModel.TableName, createModel.Columns, row);
                            using (IDbCommand insertCmd = conn.CreateCommand())
                            {
                                insertCmd.CommandText = insertSql;
                                for (int i = 0; i < createModel.Columns.Count; i++)
                                {
                                    var param = insertCmd.CreateParameter();
                                    param.ParameterName = GetParameterName(dbType, i);
                                    param.Value = i < row.Count ? (object)row[i] : DBNull.Value;
                                    insertCmd.Parameters.Add(param);
                                }
                                insertCmd.ExecuteNonQuery();
                            }
                        }
                    }

                    // ✅ FIX: use proper disposable adapters
                    var dt = new DataTable();
                    switch (dbType.ToLower())
                    {
                        case "sqlserver":
                            using (var adapter = new SqlDataAdapter($"SELECT * FROM {EscapeIdentifier(dbType, createModel.TableName)}", (SqlConnection)conn))
                                adapter.Fill(dt);
                            break;

                        case "mysql":
                            using (var adapter = new MySqlDataAdapter($"SELECT * FROM {EscapeIdentifier(dbType, createModel.TableName)}", (MySqlConnection)conn))
                                adapter.Fill(dt);
                            break;

                        case "postgresql":
                            using (var adapter = new NpgsqlDataAdapter($"SELECT * FROM {EscapeIdentifier(dbType, createModel.TableName)}", (NpgsqlConnection)conn))
                                adapter.Fill(dt);
                            break;

                        case "oracle":
                            using (var adapter = new OracleDataAdapter($"SELECT * FROM {EscapeIdentifier(dbType, createModel.TableName)}", (OracleConnection)conn))
                                adapter.Fill(dt);
                            break;
                    }

                    ViewBag.ReadTable = dt;
                }

                ViewBag.Success = true;
                ViewBag.Message = "Database/table created and data inserted successfully.";
                return View("CreateData", createModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateData failed");
                ModelState.AddModelError("", $"Operation failed: {ex.Message}");
                return View("CreateData", createModel);
            }
        }

        #region Helper Methods

        private void CreateDatabaseIfNotExists(string dbType, string server, string database, string user, string password)
        {
            string adminConnStr = GetAdminConnectionStringForCreation(dbType, server, user, password);
            using (IDbConnection conn = GetDbConnection(dbType, adminConnStr))
            {
                conn.Open();

                string checkSql = dbType.ToLower() switch
                {
                    "sqlserver" => $"IF DB_ID(N'{EscapeSqlLiteral(database)}') IS NULL CREATE DATABASE {EscapeIdentifier(dbType, database)};",
                    "mysql" => $"CREATE DATABASE IF NOT EXISTS `{EscapeSqlLiteral(database)}`;",
                    "postgresql" => $"SELECT 1 FROM pg_database WHERE datname = '{EscapeSqlLiteral(database)}';",
                    _ => throw new NotSupportedException("Database creation not supported.")
                };

                if (dbType.ToLower() == "postgresql")
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = checkSql;
                        var result = cmd.ExecuteScalar();
                        if (result == null)
                        {
                            using (var createCmd = conn.CreateCommand())
                            {
                                createCmd.CommandText = $"CREATE DATABASE \"{database}\";";
                                createCmd.ExecuteNonQuery();
                            }
                        }
                    }
                }
                else
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = checkSql;
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private string GetAdminConnectionStringForCreation(string dbType, string server, string user, string password)
        {
            return dbType.ToLower() switch
            {
                "sqlserver" => $"Server={server};Database=master;User Id={user};Password={password};TrustServerCertificate=True;",
                "mysql" => $"Server={server};Uid={user};Pwd={password};",
                "postgresql" => $"Host={server};Database=postgres;Username={user};Password={password};",
                _ => throw new NotSupportedException("DB creation not supported.")
            };
        }

        private string BuildCreateTableSql(string dbType, string tableName, List<string> columns)
        {
            if (columns == null || columns.Count == 0)
                throw new ArgumentException("At least one column required.");

            var cols = new List<string>();
            foreach (var col in columns)
                cols.Add($"{EscapeIdentifier(dbType, SanitizeIdentifier(col))} VARCHAR(500)");

            return $"CREATE TABLE {EscapeIdentifier(dbType, tableName)} ({string.Join(", ", cols)});";
        }

        private string BuildInsertSql(string dbType, string tableName, List<string> columns, List<string> values)
        {
            var colNames = new List<string>();
            var paramNames = new List<string>();
            for (int i = 0; i < columns.Count; i++)
            {
                colNames.Add(EscapeIdentifier(dbType, SanitizeIdentifier(columns[i])));
                paramNames.Add(GetParameterPlaceholder(dbType, i));
            }

            return $"INSERT INTO {EscapeIdentifier(dbType, tableName)} ({string.Join(", ", colNames)}) VALUES ({string.Join(", ", paramNames)});";
        }

        private string GetParameterPlaceholder(string dbType, int index)
            => dbType.ToLower() == "oracle" ? $":p{index}" : $"@p{index}";

        private string GetParameterName(string dbType, int index)
            => dbType.ToLower() == "oracle" ? $":p{index}" : $"@p{index}";

        private IDbConnection GetDbConnection(string dbType, string connectionString) => dbType.ToLower() switch
        {
            "sqlserver" => new SqlConnection(connectionString),
            "mysql" => new MySqlConnection(connectionString),
            "postgresql" => new NpgsqlConnection(connectionString),
            "oracle" => new OracleConnection(connectionString),
            _ => throw new NotSupportedException("Unsupported database type")
        };

        private string GetConnectionString(string dbType, string server, string database, string user, string password) => dbType.ToLower() switch
        {
            "sqlserver" => $"Server={server};Database={database};User Id={user};Password={password};TrustServerCertificate=True;",
            "mysql" => $"Server={server};Database={database};User Id={user};Password={password};",
            "postgresql" => $"Host={server};Database={database};Username={user};Password={password};",
            "oracle" => $"Data Source={server};User Id={user};Password={password};",
            _ => null
        };

        private string SanitizeIdentifier(string name)
            => string.IsNullOrWhiteSpace(name) ? "col" : Regex.Replace(name.Trim(), @"[^\w]", "_");

        private string EscapeIdentifier(string dbType, string identifier) => dbType.ToLower() switch
        {
            "sqlserver" => $"[{identifier}]",
            "mysql" => $"`{identifier}`",
            "postgresql" => $"\"{identifier}\"",
            "oracle" => identifier,
            _ => identifier
        };

        private string EscapeSqlLiteral(string s) => s?.Replace("'", "''") ?? s;
        #endregion
    }
}
