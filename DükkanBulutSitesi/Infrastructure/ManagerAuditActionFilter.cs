using Entity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;

namespace DükkanBulutSitesi.Infrastructure
{
    public class ManagerAuditActionFilter : IAsyncActionFilter
    {
        private readonly AppDbContext _db;
        private readonly CurrentStoreAccessor _storeAccessor;

        public ManagerAuditActionFilter(AppDbContext db, CurrentStoreAccessor storeAccessor)
        {
            _db = db;
            _storeAccessor = storeAccessor;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Run action first so we can choose to log based on result
            var executed = await next();

            // Only log ManagerController (safety)
            var controllerName = context.RouteData.Values["controller"]?.ToString();
            if (!string.Equals(controllerName, "Manager", StringComparison.OrdinalIgnoreCase))
                return;

            // User id
            var userIdStr = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId))
                return;

            // Store id (current store)
            var storeId = _storeAccessor.CurrentStoreId;
            if (storeId == 0)
                return;

            var actionName = context.RouteData.Values["action"]?.ToString() ?? "";
            var method = context.HttpContext.Request.Method ?? "";

            var req = context.HttpContext.Request;

            string? ip = context.HttpContext.Connection.RemoteIpAddress?.ToString();
            string? queryString = req.QueryString.HasValue ? req.QueryString.Value : null;

            // Include route values too (often contains "id")
            var routeVals = context.RouteData.Values;

            // Allow actions to override the auto-generated description
            string? desc = null;

            if (context.HttpContext.Items.TryGetValue("AuditDescription", out var forced) &&
                forced is string forcedStr &&
                !string.IsNullOrWhiteSpace(forcedStr))
            {
                desc = forcedStr;
            }
            else
            {
                desc = BuildCompactArgs(context.ActionArguments, routeVals);
            }

            _db.ManagerAuditLogs.Add(new ManagerAuditLog
            {
                StoreID = storeId,
                ManagerUserID = userId,
                CreatedAtUtc = DateTime.UtcNow,

                Controller = controllerName ?? "Manager",
                Action = actionName,
                HttpMethod = method,

                Path = req.Path.Value,
                QueryString = queryString,

                Description = desc,
                IpAddress = ip
            });

            await _db.SaveChangesAsync();
        }

        private static string? BuildCompactArgs(
            IDictionary<string, object?> actionArgs,
            RouteValueDictionary routeVals)
        {
            // We'll store as: key=value, key2=value2 ...
            // Owner-side ParseArgs relies on this format.
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Route values first (often stable: id)
            foreach (var kv in routeVals)
            {
                if (kv.Value == null) continue;

                var key = kv.Key;
                if (key.Equals("controller", StringComparison.OrdinalIgnoreCase)) continue;
                if (key.Equals("action", StringComparison.OrdinalIgnoreCase)) continue;

                var val = FormatValue(kv.Value);
                if (!string.IsNullOrWhiteSpace(val))
                    dict[key] = val;
            }

            // ActionArguments next
            foreach (var kv in actionArgs)
            {
                if (kv.Value == null) continue;

                // Basic safety: skip obvious sensitive keys
                if (kv.Key.Contains("password", StringComparison.OrdinalIgnoreCase)) continue;
                if (kv.Key.Contains("token", StringComparison.OrdinalIgnoreCase)) continue;

                // Flatten objects into multiple keys when appropriate
                FlattenInto(dict, kv.Key, kv.Value);
            }

            if (dict.Count == 0) return null;

            // Keep it compact + stable ordering
            var parts = dict
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key}={kv.Value}")
                .ToList();

            var joined = string.Join(", ", parts);

            // clamp so DB doesn't balloon
            const int maxLen = 900; // adjust if needed
            if (joined.Length > maxLen)
                joined = joined.Substring(0, maxLen) + "…";

            return joined;
        }

        private static void FlattenInto(Dictionary<string, string> dict, string key, object value)
        {
            if (value == null) return;

            // Direct primitives (store as key=value)
            if (IsSimple(value.GetType()))
            {
                var v = FormatValue(value);
                if (!string.IsNullOrWhiteSpace(v))
                    dict[key] = v;
                return;
            }

            // Files: do NOT log contents, only metadata
            if (value is IFormFile file)
            {
                dict[key] = $"File(Name={TrimForLog(file.FileName, 60)}, Size={file.Length})";
                return;
            }

            // Collections (avoid dumping huge lists)
            if (value is IEnumerable enumerable && value is not string)
            {
                int i = 0;
                foreach (var it in enumerable)
                {
                    if (it == null) continue;
                    i++;
                    if (i >= 1) break;
                }
                dict[key] = $"[{i}+ items]";
                return;
            }

            // Complex object: extract SIMPLE public properties into flat keys
            // Example: SelectedOffer.Start -> SelectedOfferStart=...
            var t = value.GetType();
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .Where(p => IsSimple(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))
                .ToList();

            if (props.Count == 0)
            {
                // fallback to type name only
                dict[key] = t.Name;
                return;
            }

            // Prefer "important" fields first; but still include more if available
            string[] preferred =
            {
                "Id","ID",
                "StoreID","storeId",
                "PersonelID","personelId","SelectedPersonelId","targetPersonelId",
                "ItemID","itemId",
                "CommentID","commentId",
                "ComplaintID","complaintId",
                "IssueRequestID","issueRequestId",
                "ConversationID","conversationId",
                "status","Status",
                "message","firstMessage"
            };

            var ordered = props
                .OrderByDescending(p => preferred.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Take(20); // keep reasonable

            foreach (var p in ordered)
            {
                object? pv;
                try { pv = p.GetValue(value); }
                catch { continue; }

                if (pv == null) continue;

                var pvStr = FormatValue(pv);
                if (string.IsNullOrWhiteSpace(pvStr)) continue;

                // Flatten into top-level keys so Owner ParseArgs can read them
                // Example: vm.PersonelID -> PersonelID=123
                // Avoid overwriting if already present from route values
                if (!dict.ContainsKey(p.Name))
                    dict[p.Name] = pvStr;

                // Also store "prefix+Prop" if you want more context (optional)
                // This helps when multiple VMs have same property names.
                var prefKey = key + p.Name; // e.g. SelectedOfferStart
                if (!dict.ContainsKey(prefKey))
                    dict[prefKey] = pvStr;
            }
        }

        private static bool IsSimple(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;

            return t == typeof(string)
                || t == typeof(int)
                || t == typeof(long)
                || t == typeof(short)
                || t == typeof(byte)
                || t == typeof(bool)
                || t == typeof(decimal)
                || t == typeof(double)
                || t == typeof(float)
                || t == typeof(DateTime)
                || t == typeof(Guid);
        }

        private static string FormatValue(object value)
        {
            if (value == null) return "";

            if (value is string s)
                return TrimForLog(s, 160);

            if (value is bool b)
                return b ? "true" : "false";

            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            if (value is Guid g)
                return g.ToString("D");

            if (value is int or long or short or byte or decimal or double or float)
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

            // fallback
            return TrimForLog(value.ToString() ?? "", 160);
        }

        private static string TrimForLog(string s, int maxLen)
        {
            s = (s ?? "").Trim();
            if (s.Length <= maxLen) return s;
            return s.Substring(0, maxLen) + "…";
        }
    }
}
