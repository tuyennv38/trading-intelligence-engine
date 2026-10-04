using System;
using Octonica.ClickHouseClient;

class Program {
    static void Main() {
        try {
            using var conn = new ClickHouseConnection(""Host=gsmdev.cscmobicorp.com;Port=9000;User=rootadmin;Password=cscDbDev2015;Database=gsm_logs"");
            conn.Open();
            Console.WriteLine(""Success"");
        } catch (Exception ex) {
            Console.WriteLine($""Error: {ex.Message}"");
        }
    }
}
