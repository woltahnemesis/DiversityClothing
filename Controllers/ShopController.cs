using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiversityClothing.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;

namespace DiversityClothing.Controllers
{
    public class ShopController : Controller
    {
        //Add database connection
        private readonly DiversityClothingContext _context;
        public ShopController(DiversityClothingContext context)
        {
            _context = context;
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

            //Create and save a new Cart Object
            var cart = new Cart
            {
                ProductId = ProductId,
                Quantity = Quantity,
                Price = price,
                Username = cartUserName
            };

            _context.Cart.Add(cart);
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
        public IActionResult Checkout([Bind("FirstName, LastName, Address, City, Province, PostalCode, Phone")] Order order) 
        {
            //autofill the date, User, and total properties instead of the user inputing these values
            order.OrderDate = DateTime.Now;
            order.UserId = User.Identity.Name;

            var cartItems = _context.Cart.Where(c => c.Username == User.Identity.Name);
            decimal cartTotal = (from c in cartItems select c.Quantity * c.Price).Sum();
            order.Total = cartTotal;

            HttpContext.Session.SetString("cartTotal", cartTotal.ToString());

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

    }
}
