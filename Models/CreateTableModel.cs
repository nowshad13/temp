// Models/CreateTableModel.cs
using System.Collections.Generic;

namespace DbConnectionTester.Models
{
    public class CreateTableModel
    {
        public string DatabaseName { get; set; } = "";
        public string TableName { get; set; } = "";
        public List<string> Columns { get; set; } = new List<string>();
        // Rows: list of rows where each row is a list of cell values (strings)
        public List<List<string>> Rows { get; set; } = new List<List<string>>();
    }
}
