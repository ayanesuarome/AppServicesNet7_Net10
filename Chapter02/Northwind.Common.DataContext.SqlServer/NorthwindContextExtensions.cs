using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Packt.Shared
{
    public static class NorthwindContextExtensions
    {
        public static IServiceCollection AddNorthwindContext(this IServiceCollection services, 
            string connectionString = "Server=localhost,1433;Database=Northwind;User Id=sa;Password=MiPasswordDeDesarrollo.Net123!;TrustServerCertificate=True;")
        {
            services.AddDbContext<NorthwindContext>(options =>
            {
                options.UseSqlServer(connectionString);
                options.LogTo(Console.WriteLine, new[] { Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuting });
            });

            return services;
        }
    }
}
