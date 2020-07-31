using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiversityClothing.Models;
using Microsoft.AspNetCore.Mvc;

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

            //Create and save a new Cart Object
            var cart = new Cart
            {
                ProductId = ProductId,
                Quantity = Quantity,
                Price = price,
                Username = "tempUser"
            };

            _context.Cart.Add(cart);
            _context.SaveChanges();

            //Show cart page
            return RedirectToAction("Cart");
        }
    }
}
