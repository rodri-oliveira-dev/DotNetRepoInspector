namespace Fixture.DataDependencies;

internal sealed class Relational
{
    public void Configure(IServiceCollection services, DbContextOptionsBuilder options, IConfiguration configuration)
    {
        services.AddNpgsqlDataSource(configuration.GetConnectionString("LoanDb"));
        options.UseNpgsql(configuration.GetConnectionString("LoanDb"));
        options.UseSqlServer("Server=PRIVATE-SQL-SERVER;User Id=private-user;Password=PRIVATE-PASSWORD");
        services.AddMySqlDataSource(configuration.GetConnectionString("OrdersMySql"));
        options.UseOracle(configuration.GetConnectionString("LedgerOracle"));
    }

    public async Task QueryAsync(SqlCommand sql, MySqlCommand mysql, OracleCommand oracle, NpgsqlCommand postgres)
    {
        sql.CommandText = "PRIVATE SQL QUERY";
        await sql.ExecuteReaderAsync();
        await mysql.ExecuteNonQueryAsync();
        await oracle.ExecuteReaderAsync();
        await postgres.ExecuteNonQueryAsync();
    }
}
