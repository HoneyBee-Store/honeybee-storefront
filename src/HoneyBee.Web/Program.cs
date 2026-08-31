using HoneyBee.Web.Data;
using HoneyBee.Web.Models;
using HoneyBee.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using System.Globalization;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Views resolve UI text through IStringLocalizer<SharedResource>, which reads
// Resources/SharedResource.{culture}.resx. Not IViewLocalizer — that looks for
// a resource file per view instead of one shared table.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// Arabic is the default and English is opt-in, matching v1. Product text comes
// from per-language columns rather than resources, so the server needs to know
// the culture before it can render a page at all.
var supportedCultures = new[] { new CultureInfo("ar"), new CultureInfo("en") };
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("ar");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;

    // Only honour an explicit choice. Without this, an English browser
    // Accept-Language header would silently override the Arabic default.
    options.RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new CookieRequestCultureProvider()
    };
});

// The basket lives in session — scratch data most visitors abandon, so it
// only reaches the database at checkout.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(4);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddSingleton(builder.Configuration.GetSection("Shop").Get<ShopSettings>() ?? new ShopSettings());
builder.Services.AddSingleton(builder.Configuration.GetSection("Smtp").Get<SmtpSettings>() ?? new SmtpSettings());
builder.Services.AddScoped<OrderNotifier>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Admin sign-in only. IdentityCore rather than DefaultIdentity because the
// latter scaffolds a full self-service account UI — register, forgot password,
// 2FA — and none of that belongs on a shop where customers never sign in.
// Phase 2 adds a single hand-written login page against SignInManager.
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 10;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    // Customers, not the owner: almost every redirect here is a shopper
    // hitting checkout. The owner reaches /Admin/Login directly.
    options.LoginPath = "/Account/Login";
    // Not the login page: someone hitting this is already signed in, and
    // being asked to sign in again explains nothing.
    options.AccessDeniedPath = "/Account/Denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRequestLocalization(
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>().Value);

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Apply migrations and import the v1 catalogue on first run. Fine at this
// size; if the app is ever scaled to more than one instance, move this to a
// deploy step so two instances can't migrate at the same time.
await using (var scope = app.Services.CreateAsyncScope())
{
    var sp = scope.ServiceProvider;

    var db = sp.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);

    await AdminSeeder.SeedAsync(
        sp.GetRequiredService<UserManager<AppUser>>(),
        sp.GetRequiredService<RoleManager<IdentityRole>>(),
        app.Configuration,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("AdminSeeder"));
}

app.Run();
