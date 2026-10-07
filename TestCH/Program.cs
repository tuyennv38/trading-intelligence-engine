using System;
using System.Threading;
using System.Threading.Tasks;
using Octonica.ClickHouseClient;

class Program {
    static async Task Main() {
        try {
            Console.WriteLine("STARTING NATIVE GSM DB TEST AS SYNC...");
            var csBuilder = new ClickHouseConnectionStringBuilder("Host=localhost;Port=9000;User=default;Password=;Database=default");
            await using var conn = new ClickHouseConnection(csBuilder.ConnectionString);
            Console.WriteLine("Calling Open()...");
            conn.Open();
            Console.WriteLine(">>> Open() COMPLETED SUCCESSFULLY!");
        } catch (Exception ex) {
            Console.WriteLine(">>> Error: " + ex.Message);
        }
    }
}
