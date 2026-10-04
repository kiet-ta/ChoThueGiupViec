using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace CommonService.WebAPI.Controllers;
[Route("api/[controller]")]
[ApiController]
public class PaymentIntent : ControllerBase
{
    [HttpPost("create-paymen-intent")]
    public IActionResult CreatePaymentIntent()
    {
        var options = new Stripe.PaymentIntentCreateOptions
        {
            Amount = 1099,
            Currency = "usd",
            // Verify your integration in this guide by including this parameter
            Metadata = new Dictionary<string, string>
            {
              { "integration_check", "accept_a_payment" },
            },
            PaymentMethodTypes = new List<string>
            {
              "card",
            },
        };
        var service = new PaymentIntentService();
        var intent = service.Create(options);

        return Ok(new { clientSecret = intent.ClientSecret });

    }

}
