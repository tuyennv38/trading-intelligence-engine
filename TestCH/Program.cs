using System;
using System.Threading.Tasks;
using Octonica.ClickHouseClient;

class Program {
    static async Task Main() {
        try {
            var connStr = "Host=eventlogdev.cscmobicorp.com;Port=9000;User=rootadmin;Password=cscDbDev2015;Database=gsm_logs";
            Console.WriteLine("Connecting to eventlogdev...");
            await using var conn = new ClickHouseConnection(connStr);
            await conn.OpenAsync();
            Console.WriteLine("Connect OK");
        } catch (Exception ex) {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
