using System.Text.Json.Nodes;
using Pos.Application;
using Pos.Application.Ports;

namespace Pos.Infrastructure.Odoo;

// Adapter: cashiers are Odoo users. They are created, disabled and given
// permissions in Odoo (Settings > Users); this API keeps no user list.
//  1. Odoo checks login + password.
//  2. With the integration API key we check the "Point of Sale / User" group.
public sealed class OdooOperatorDirectory(OdooJson2Client odoo) : IOperatorDirectory
{
    public const string PosUserGroup = "point_of_sale.group_pos_user";

    public async Task<Operator?> ValidateCredentialsAsync(string username, string password, CancellationToken ct)
    {
        int? uid = await odoo.AuthenticateAsync(username, password, ct);
        if (uid is null)
            return null;

        var hasPos = await odoo.CallAsync("res.users", "has_group",
            new { ids = new[] { uid.Value }, group_ext_id = PosUserGroup }, ct);
        if (hasPos is not JsonValue v || !v.TryGetValue<bool>(out bool allowed) || !allowed)
            throw new OperatorNotAuthorizedException();

        var rows = await odoo.CallAsync("res.users", "search_read",
            new { domain = new object[] { new object[] { "id", "=", uid.Value } }, fields = new[] { "name", "login" } }, ct)
            as JsonArray;
        var user = rows?.FirstOrDefault();
        string login = user?["login"]?.GetValue<string>() ?? username;
        string name = user?["name"]?.GetValue<string>() ?? login;

        return new Operator(uid.Value, login, name);
    }
}
