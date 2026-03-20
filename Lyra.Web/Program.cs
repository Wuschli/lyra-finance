using Lyra.Core;
using Lyra.Core.Services;
using Lyra.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;

namespace Lyra.Web;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo("/var/lib/lyra/keys"))
            .SetApplicationName("Lyra");

        // Add services to the container.
        builder.Services
            .AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(
                builder.Configuration.GetConnectionString("postgres")!)
            )
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Authority = builder.Configuration["Zitadel:Authority"];
                options.ClientId = builder.Configuration["Zitadel:ClientId"];
                options.ResponseType = "code";
                options.UsePkce = true;

                options.CallbackPath = "/signin-oidc";
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
            });

        builder.Services.AddAuthorization()
            .AddCascadingAuthenticationState();

        builder.Services
            .AddScoped<UserService>()
            .AddScoped<AccountService>();
        builder.Services.AddTransient<IClaimsTransformation, UserClaimsTransformation>();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddControllers();

        var app = builder.Build();

        // Configure Dapper
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapControllers();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}