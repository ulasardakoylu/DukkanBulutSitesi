using DükkanBulutSitesi.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Entity;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

namespace DükkanBulutSitesi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            QuestPDF.Settings.License = LicenseType.Community;

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(
                    builder.Configuration.GetConnectionString("DefaultConnection"),
                    sql =>
                    {
                        sql.MigrationsAssembly("Entity");
                        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                        sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                        sql.CommandTimeout(60);
                    }));



            builder.Services.AddControllersWithViews();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSession();
            builder.Services.AddScoped<CurrentStoreAccessor>();
            builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
            builder.Services.AddScoped<IPermissionService, PermissionService>();
            builder.Services.AddScoped<IAuthorizationHandler, ActivePersonnelHandler>();

            builder.Services.AddScoped<ManagerAuditActionFilter>();

            builder.Services.AddControllersWithViews(o =>
            {
                o.Filters.AddService<ManagerAuditActionFilter>();
            });


            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("ActivePersonnel",
                    p => p.Requirements.Add(new ActivePersonnelRequirement()));

                options.AddPolicy("Perm:StockControl",
                    p => p.Requirements.Add(new PermissionRequirement("personnel.stock_control")));

                options.AddPolicy("Perm:QuickWorkPanel",
                    p => p.Requirements.Add(new PermissionRequirement("personnel.quick_work_panel")));

                options.AddPolicy("Perm:SecurityWorkPanel",
                    p => p.Requirements.Add(new PermissionRequirement("personnel.security_work_panel")));
            });


            builder.Services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(o =>
                {
                    o.LoginPath = "/auth/login";     
                    o.LogoutPath = "/auth/logout";
                    o.AccessDeniedPath = "/";      
                    o.SlidingExpiration = true;
                    o.ExpireTimeSpan = TimeSpan.FromDays(7);
                });

            builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
            builder.Services.AddSingleton<IAuditLogger, FileAuditLogger>();

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseSession();
            app.UseAuthentication();
            app.UseAuthorization();

            app.Use(async (ctx, next) =>
            {
                const string cookieName = "CurrentStoreId";

                if (ctx.Request.Query.TryGetValue("storeId", out var sid) &&
                    int.TryParse(sid, out var gidQ))
                {
                    ctx.Response.Cookies.Append(cookieName, gidQ.ToString(),
                        new CookieOptions { HttpOnly = true, IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(30) });
                }

                if (ctx.Request.Cookies.TryGetValue(cookieName, out var c) &&
                    int.TryParse(c, out var gidC))
                    ctx.Items[cookieName] = gidC;
                else
                    ctx.Items[cookieName] = 0;

                await next();
            });

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Welcome}/{id?}");

            app.Run();
        }
    }
}
