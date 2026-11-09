using Microsoft.AspNetCore.Mvc;
using Northwind.WebApi.Client.Mvc.Models;
using Packt.Shared;
using System.Diagnostics;

namespace Northwind.WebApi.Client.Mvc.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IHttpClientFactory _clientFactory;

        public HomeController(ILogger<HomeController> logger,
            IHttpClientFactory clientFactory)
        {
            _logger = logger;
            _clientFactory = clientFactory;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [Route("home/products/{name?}")]
        public async Task<IActionResult> Products([FromQuery] string? name)
        {
            HttpClient client = _clientFactory.CreateClient(
            name: "Northwind.WebApi.Service");
            HttpRequestMessage request = new(
            method: HttpMethod.Get, requestUri: $"api/products/{name}");
            HttpResponseMessage response = await client.SendAsync(request);

            if(response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                string retryAfter = response.Headers.GetValues("Retry-After").ToArray()[0];

                if(int.TryParse(retryAfter, out int waitFor))
                {
                    WriteLine($"Retry after {waitFor} seconds.");
                    return Error();
                }
            }

            IEnumerable<Product>? model = await response.Content
            .ReadFromJsonAsync<IEnumerable<Product>>();
            
            ViewData["baseaddress"] = client.BaseAddress;
            return View(model);
        }

        [Route("home/products")]
        public async Task<IActionResult> Products()
        {
            HttpClient client = _clientFactory.CreateClient(
            name: "Northwind.WebApi.Service");
            HttpRequestMessage request = new(
            method: HttpMethod.Get, requestUri: $"api/products");
            HttpResponseMessage response = await client.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                string retryAfter = response.Headers.GetValues("Retry-After").ToArray()[0];

                if (int.TryParse(retryAfter, out int waitFor))
                {
                    WriteLine($"Retry after {waitFor} seconds.");
                    return Error();
                }
            }

            IEnumerable<Product>? model = await response.Content
            .ReadFromJsonAsync<IEnumerable<Product>>();

            ViewData["baseaddress"] = client.BaseAddress;
            return View(model);
        }
    }
}