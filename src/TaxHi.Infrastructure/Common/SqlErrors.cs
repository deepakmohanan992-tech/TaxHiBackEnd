using Microsoft.Data.SqlClient;

namespace TaxHi.Infrastructure.Common;

public static class SqlErrors
{
    /// <summary>The command timed out (client-side timeout error -2, or a lock/timeout raised by the engine).</summary>
    public static bool IsTimeout(SqlException ex) =>
        ex.Number == -2 || ex.InnerException is TimeoutException || ex.Message.Contains("Timeout expired", StringComparison.OrdinalIgnoreCase);
}
