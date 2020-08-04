using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiversityClothing.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SQLitePCL;
using Stripe;
using Stripe.Checkout;

namespace DiversityClothing.Controllers
{
    public class ShopController : Controller
    {
        //Add database connection
        private readonly DiversityClothingContext _context;

        //Add configuration so controller can read config value appsettings.json
        private IConfiguration _configuration;

        public ShopController(DiversityClothingContext context, IConfiguration configuration) //Dependency Injection
        {
            //Accept an instance of our DB connection class and use this object connection.
            _context = context;

            //Accept an instance of the configuration object
            _configuration = configuration;
        }
        //GET Index
        public IActionResult Index()
        {
            //Return list of categories for the user to browse
            var categories = _context.Category.OrderBy(c => c.Name).ToList();
            return View(categories);
        }
        //GET Browse
        public IActionResult Browse(string category)
        {
            //Store the selected category name in the ViewBag so we can display in the View heading
            ViewBag.Category = category;

            //Get the list of products for the selected category and pass the list to the View
            var products = _context.Product.Where(p => p.Category.Name == category).OrderBy(products => products.Name).ToList();
            return View(products);
        }

        //GET ProductDetails
        public IActionResult ProductDetails(string product)
        {
            //Use a SingleorDefault to find either 1 exact match or a null object
            var selectedProduct = _context.Product.SingleOrDefault(p => p.Name == product);
            return View(selectedProduct);
        }

        //POST Add To Cart
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddToCart(int Quantity, int ProductId)
        {
            //Identify product price
            var product = _context.Product.SingleOrDefault(p => p.ProductId == ProductId);
            var price = product.Price;

            //Determine username
            var cartUserName = GetCartUsername();

            //Check if THIS USER's product already exists in the cart. If so, update the quantity
            var cartItem = _context.Cart.SingleOrDefault(c => c.ProductId == ProductId && c.Username == cartUserName);

            if(cartItem == null)
            {
                //Create and save a new Cart Object
                var cart = new Cart
                {
                    ProductId = ProductId,
                    Quantity = Quantity,
                    Price = price,
                    Username = cartUserName
                };
                _context.Cart.Add(cart);
            } 
            else
            {
                cartItem.Quantity += Quantity;
                _context.Update(cartItem);
            }

            _context.SaveChanges();

            //Show cart page
            return RedirectToAction("Cart");
        }

        //Check or set Cart username
        private string GetCartUsername()
        {
            //1. Check if we already stored in the Username in the User's session
            if (HttpContext.Session.GetString("CartUserName") == null)
            {
                //Initialize an empty string variable that will later add to the Session Object
                var cartUserName = "";

                //Check if user is authenticated
                if (User.Identity.IsAuthenticated)
                {
                    cartUserName = User.Identity.Name;
                }
                else
                {
                    //If not, use the GUID class to make a new ID and store that in the session.
                    cartUserName = Guid.NewGuid().ToString();
                }
                //Next, store the cartUserName in a session var
                HttpContext.Session.SetString("CartUserName", cartUserName);
            }

            //return session name
            return HttpContext.Session.GetString("CartUserName");
        }

        public IActionResult Cart()
        {
            //1. Figure out who the user is
            var cartUserName = GetCartUsername();

            //2. Query the database to get the user's cart items
            var cartItems = _context.Cart.Include(c => c.Product).Where(c => c.Username == cartUserName).ToList();

            //3. Load a view to pass the cart items for display
            return View(cartItems);
        }

        public IActionResult RemoveFromCart(int id)
        {
            //Get the user wants to delete
            var cartItem = _context.Cart.SingleOrDefault(c => c.CartId == id);

            //Delete the object
            _context.Cart.Remove(cartItem);
            _context.SaveChanges();

            return RedirectToAction("Cart");
        }

        // SET
        [Authorize]
        public IActionResult Checkout()
        {
            //Checkout if the user has been shopping anonymously now that they are logged in
            MigrateCart();
            return View();
        }

        // GET
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Checkout([Bind("FirstName, LastName, Address, City, Province, PostalCode, Phone")] Models.Order order) 
        {
            //autofill the date, User, and total properties instead of the user inputing these values
            order.OrderDate = DateTime.Now;
            order.UserId = User.Identity.Name;

            var cartItems = _context.Cart.Where(c => c.Username == User.Identity.Name);
            decimal cartTotal = (from c in cartItems select c.Quantity * c.Price).Sum();
            order.Total = cartTotal;

            //HttpContext.Session.SetString("cartTotal", cartTotal.ToString());

            //We now have the Session to the complex object
            HttpContext.Session.SetObject("Order", order);

            return RedirectToAction("Payment");
        }

        private void MigrateCart()
        {
            //If user has shopped without an account, attach their items to their name
            if(HttpContext.Session.GetString("CartUserName") != User.Identity.Name)
            {
                var cartUsername = HttpContext.Session.GetString("CartUserName");
                //get the user's cart items
                var cartItems = _context.Cart.Where(c => c.Username == cartUsername);

                //loop through the cart items and update the username for each one
                foreach (var item in cartItems)
                {
                    item.Username = User.Identity.Name;
                    _context.Update(item);
                }
                _context.SaveChanges();

                //Update the session variable from a GUID to the user's email
                HttpContext.Session.SetString("CartUserName", User.Identity.Name);
            }

        }

        public IActionResult Payment()
        {
            //Setup payment page to show order total

            //1. Get the order from the session variable & cast as an order object
            var order = HttpContext.Session.GetObject<Models.Order>("Order");

            //2. Use Viewbag to display total and pass the amount to strip
            ViewBag.Total = order.Total;
            ViewBag.CentsTotal = order.Total * 100; //Stripe requires amount in cents, not dollars and cents
            ViewBag.PublishableKey = _configuration.GetSection("Stripe")["PublishableKey"];

            return View();
        }

        //Need to get 2 things back from Stripe after the authorization
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Payment(string stripeEmail, string stripeToken)
        {
            //Send payment to stripe
            StripeConfiguration.ApiKey = _configuration.GetSection("Stripe")["SecretKey"];
            var cartUsername = HttpContext.Session.GetString("CartUsername");
            var cartItems = _context.Cart.Where(c => c.Username == cartUsername);
            var order = HttpContext.Session.GetObject<Models.Order>("Order");

            //new Stripe payment attempt
            var customerService = new CustomerService();
            var chargeService = new ChargeService();
            //new customer email from payment form, token auto-generated on payment form also
            var customer = customerService.Create(new CustomerCreateOptions
            {
                Email = stripeEmail,
                Source = stripeToken
            });

            //new charge using customer created above
            var charge = chargeService.Create(new ChargeCreateOptions
            {
                Amount = Convert.ToInt32(order.Total * 100),
                Description = "Diversity Clothing Purchase",
                Currency = "cad",
                Customer = customer.Id
            });

            //Generate and save new order
            _context.Order.Add(order);
            _context.SaveChanges();

            //Save order details
            foreach (var item in cartItems)
            {
                var orderDetail = new OrderDetail
                {
                    OrderId = order.OrderId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    Price = item.Price
                };

                _context.OrderDetail.Add(orderDetail);
            }
            _context.SaveChanges();

            //Delete cart
            foreach (var item in cartItems)
            {
                _context.Cart.Remove(item);
            }

            _context.SaveChanges();

            //Confirm with a receipt for the new Order Id

            return RedirectToAction("Details", "Orders", new { id = order.OrderId });
        }

    }
}
