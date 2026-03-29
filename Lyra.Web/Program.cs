using DbUp;
using Lyra.Core;
using Lyra.Core.Auth;
using Lyra.Core.Services;
using Lyra.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Lyra.Web;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (!DoMigration(builder))
            return;

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
            .AddScoped<AccountService>()
            .AddScoped<EnableBankingService>()
            .AddSingleton<EnableBankingAccessTokenProvider>();

        builder.Services.AddTransient<IClaimsTransformation, UserClaimsTransformation>();

        builder.Services.AddScoped(sp =>
        {
            var tokenProvider = sp.GetRequiredService<EnableBankingAccessTokenProvider>();
            var authProvider = new BaseBearerTokenAuthenticationProvider(tokenProvider);
            var adapter = new HttpClientRequestAdapter(authProvider);
            return new Core.EnableBanking.ApiClient(adapter);
        });


        //builder.Services.AddHttpClient<IRequestAdapter, HttpClientRequestAdapter>();

        //builder.Services.AddHttpClient<IHttpClientRequestAdapter>(client =>
        //{
        //    client.BaseAddress = new Uri("https://api.enablebanking.com");
        //    // If using certificate auth, you'd configure the HttpClientHandler here
        //});

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

    private static bool DoMigration(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("postgres");

        // Make sure DB exists
        EnsureDatabase.For.PostgresqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(IDbConnectionFactory).Assembly)
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            // Log error and stop app if migration fails
            Console.WriteLine(result.Error);
        }

        return result.Successful;
    }
}