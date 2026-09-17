using EcoTrace.Identity.Api.Grpc;
using EcoTrace.Identity.Api.ErrorHandling;
using EcoTrace.Identity.Domain.Models;
using EcoTrace.Identity.Domain.Interfaces.Repositories;
using EcoTrace.Identity.Domain.Interfaces.Services;
using EcoTrace.Identity.Domain.UseCases.Services;
using EcoTrace.Identity.Infrastructure.Data;
using EcoTrace.Identity.Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcExceptionHandlingInterceptor>();
});
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDataProtection();

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("IdentityDatabase")));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;
})
.AddRoles<ApplicationRole>()
.AddRoleManager<RoleManager<ApplicationRole>>()
.AddUserManager<UserManager<ApplicationUser>>()
.AddSignInManager<SignInManager<ApplicationUser>>()
.AddEntityFrameworkStores<IdentityDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapGrpcService<IdentityGrpcServer>();
app.MapControllers();

app.Run();
