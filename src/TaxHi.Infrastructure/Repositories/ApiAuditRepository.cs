using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using TaxHi.Application.Models.Audit;
using TaxHi.Application.Repositories;
using TaxHi.Infrastructure.Common;

namespace TaxHi.Infrastructure.Repositories;

public sealed class ApiAuditRepository(IDbConnection connection, IConfiguration configuration) : IApiAuditRepository
{
    public async Task InsertAsync(ApiAuditRecord record, CancellationToken cancellationToken = default)
    {
        var p = new DynamicParameters();
        p.Add("CorrelationId", record.CorrelationId, DbType.AnsiString, size: 64);
        p.Add("RequestId", record.RequestId, DbType.AnsiString, size: 64);
        p.Add("HttpMethod", record.HttpMethod, DbType.AnsiString, size: 10);
        p.Add("ApiEndpoint", record.ApiEndpoint, DbType.String, size: 300);
        p.Add("ControllerName", record.ControllerName, DbType.String, size: 100);
        p.Add("ActionName", record.ActionName, DbType.String, size: 100);
        p.Add("RequestUrl", record.RequestUrl, DbType.String, size: 1000);
        p.Add("RequestPayload", record.RequestPayload, DbType.String);
        p.Add("RequestHeaders", record.RequestHeaders, DbType.String, size: 2000);
        p.Add("UserId", record.UserId, DbType.Int64);
        p.Add("VendorId", record.VendorId, DbType.Int32);
        p.Add("ClientIpAddress", record.ClientIpAddress, DbType.AnsiString, size: 45);
        p.Add("UserAgent", record.UserAgent, DbType.String, size: 500);
        p.Add("RequestReceivedAt", record.RequestReceivedAt, DbType.DateTime2);
        p.Add("HttpStatusCode", record.HttpStatusCode, DbType.Int16);
        p.Add("ResponsePayload", record.ResponsePayload, DbType.String);
        p.Add("ResponseReceivedAt", record.ResponseReceivedAt, DbType.DateTime2);
        p.Add("ExecutionDurationMs", record.ExecutionDurationMs, DbType.Int32);
        p.Add("ApiStatus", record.ApiStatus, DbType.AnsiString, size: 10);
        p.Add("ErrorCode", record.ErrorCode, DbType.AnsiString, size: 100);
        p.Add("ErrorMessage", record.ErrorMessage, DbType.String, size: 1000);
        p.Add("ExceptionType", record.ExceptionType, DbType.AnsiString, size: 300);
        p.Add("ExceptionDetails", record.ExceptionDetails, DbType.String, size: 4000);
        p.Add("StackTrace", record.StackTrace, DbType.String);
        p.Add("Environment", record.Environment, DbType.AnsiString, size: 30);

        await connection.ExecuteAsync(new CommandDefinition(
            "USP_InsertTaxHiBETransactionAudit", p,
            commandType: CommandType.StoredProcedure,
            commandTimeout: AuditSettings.WriteTimeoutSeconds(configuration),
            cancellationToken: cancellationToken));
    }
}
