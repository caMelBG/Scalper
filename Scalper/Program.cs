using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Scalper
{
    class Program
    {
        static void Main(string[] args)
        {
            ProcessManufactures();
        }

        static void ProcessManufactures()
        {
            string Url = "https://bghranite.eu/manufacturer/all";

            var driver = new ChromeDriver("./../../../../");

            driver.Navigate().GoToUrl(Url);

            var manufacturerElements = driver.FindElements(By.CssSelector(".manufacturer-item"));

            foreach (var manufacturerElement in manufacturerElements)
            {
                var manufacturerName = manufacturerElement.FindElement(By.CssSelector(".title a")).Text;

                var imageSrc = manufacturerElement.FindElement(By.CssSelector("img")).GetAttribute("src");

                var extension = imageSrc.Split('.').Last();

                SaveImage(imageSrc, "/manufacturers/" + manufacturerName.ToLower().Replace(' ', '-') + "." + extension);
            }
        }

        static void ProcessCategories()
        {
            string UrlFormat = "https://bghranite.eu/{0}#/";
            string[] Cats = new string[] { "храни", "за-бебето", "за-дома", "напитки", "препарати", "био", "плод-зеленчук-и-маслини" };
            var driver = new ChromeDriver("./../../../../");
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
            foreach (var cat in Cats)
            {
                driver.Navigate().GoToUrl(string.Format(UrlFormat, cat));

                //$('.picture').find('img').attr('src')
                var catElements = driver.FindElements(By.CssSelector(".sub-category-item"));

                foreach (var catElement in catElements)
                {
                    //$('.sub-category-item .title').find('a').text()
                    var catName = catElement.FindElement(By.CssSelector(".title a")).Text;

                    var imageSrc = catElement.FindElement(By.CssSelector("img")).GetAttribute("src");

                    var extension = imageSrc.Split('.').Last();

                    SaveImage(imageSrc, "/cats/" + catName.ToLower().Replace(' ', '-') + "." + extension);
                }
            }
        }

        static void MergeProductsFromAllCsvFiles()
        {
            var files = Directory.GetFiles(Directory.GetCurrentDirectory()).Where(x => x.EndsWith("csv")).Where(x => x.Contains("chunk"));
            var products = new List<Product>();
            var sb = new StringBuilder();
            var line = "";

            foreach (var file in files)
            {
                var reader = new StreamReader(file);
                while ((line = reader.ReadLine()) != null)
                {
                    //sb.AppendLine(line);

                    var parts = line.Split('\t');
                    products.Add(new Product(parts[2], parts[3], parts[4], parts[0], parts[1]));
                }
            }

            var images = new List<string>();
            products.Select(x =>
            {
                if (!File.Exists("/image/" + x.Image.Trim('\"')))
                {
                    images.Add(x.Image);
                }
                return x;
            });

            var fileExtensions = products.Select(x => x.Image.Split('.').Last()).Distinct();

            SaveCsvContent(products.Skip(1).Take(10).ToList(), 9999, 1);

            Console.WriteLine(sb);
        }

        static void ProcessProducts()
        {
            var skip = 5417;
            var take = 1000;

            var products = new List<Product>();
            var driver = new ChromeDriver("./../../../../");
            var productUrls = File.ReadAllText("result.txt").Split('\n').Skip(skip).Take(take);
            var attmepts = 0;
            while (productUrls.Any())
            {
                foreach (var url in productUrls)
                {
                    var retry = true;
                    while (retry)
                    {
                        try
                        {
                            driver.Navigate().GoToUrl(url.Trim());

                            //Task.Delay(TimeSpan.FromSeconds(1)).Wait();

                            string name = "", desc = "", imageSrc = "", imageName = "", brand = "", cat = "";

                            var nameElement = driver.FindElement(By.CssSelector(".product-name h1"));
                            var imageElement = driver.FindElement(By.CssSelector("#sevenspikes-cloud-zoom a img"));
                            IWebElement brandElement = null;
                            IWebElement catsContainerElement = null;
                            IWebElement descElement = null;

                            try
                            {
                                catsContainerElement = driver.FindElement(By.CssSelector(".breadcrumb"));
                            }
                            catch
                            {
                            }

                            try
                            {
                                brandElement = driver.FindElement(By.CssSelector(".manufacturers .value a"));
                            }
                            catch
                            {
                            }

                            try
                            {
                                descElement = driver.FindElement(By.CssSelector(".short-description"));
                            }
                            catch
                            {
                            }

                            if (nameElement != null)
                            {
                                name = nameElement.Text;
                            }

                            if (descElement != null)
                            {
                                desc = descElement.Text;
                            }
                            else if (nameElement != null)
                            {
                                desc = nameElement.Text;
                            }

                            if (imageElement != null)
                            {
                                imageSrc = imageElement.GetAttribute("src");
                                if (imageSrc != null)
                                {
                                    //TODO: Add image full path, including site url 
                                    imageName = $"{Guid.NewGuid().ToString("N")}.{imageSrc.Split('.').Last()}";
                                }
                            }

                            if (catsContainerElement != null)
                            {
                                var catsElements = catsContainerElement.FindElements(By.CssSelector("span a"));

                                cat = string.Join(">", catsElements.Skip(1).Select(x => x.Text));
                            }

                            if (brandElement != null)
                            {
                                brand = brandElement.Text;
                            }


                            products.Add(new Product(name, desc, imageName, brand, cat));

                            SaveImage(imageSrc, imageName);
                            retry = false;

                        }
                        catch (Exception ex)
                        {
                            while (ex.InnerException != null)
                            {
                                ex = ex.InnerException;
                            }

                            Console.WriteLine($"---Attemp: {++attmepts}, Error: ( {ex.Message} )");

                            if (attmepts >= 5)
                            {
                                attmepts = 1;
                                Console.WriteLine($"---Killing web driver");

                                driver.Quit();
                                driver.Dispose();

                                Task.Delay(TimeSpan.FromSeconds(5)).Wait();

                                driver = new ChromeDriver("./../../../../");
                            }
                        }
                    }
                }

                SaveCsvContent(products, skip, take);
                skip += take;
                products = new List<Product>();
                productUrls = File.ReadAllText("result.txt").Split('\n').Skip(skip).Take(take);
            }
        }

        static void SaveCsvContent(List<Product> products, int skip, int take)
        {
            var sb = new StringBuilder();

            sb.AppendLine("tax:product_tag\ttax:product_cat\tproduct_name\tshort_description\timages\tstock_status\tvisibility\tsku\tstatus");

            var sku = 10000 + (skip * take);
            foreach (var prod in products)
            {
                var imagePath = "\"https://snexport.eu/wp-content/uploads/" + prod.Image.Trim('\"') + "\"";
                sb.AppendLine($"{prod.Brand}\t{prod.Cats}\t{prod.Name}\t{prod.Desc}\t{imagePath}\tinstock\tvisible\t{sku++}\tpublish");
            }

            var fileName = $"products_chunk_{skip}.csv";

            var csvContent = sb.ToString();

            File.WriteAllText(fileName, csvContent);
        }

        static void SaveImage(string imageUrl, string imageName)
        {
            if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(imageName))
            {
                return;
            }

            using var client = new WebClient();
            client.DownloadFile(imageUrl, $"images/{imageName}");
        }

        static void ExportProductUrls()
        {
            var productUrls = new StringBuilder();
            string UrlFormat = "https://bghranite.eu/{0}#/";
            string[] Cats = new string[] { "храни", "за-бебето", "за-дома", "напитки", "препарати", "био", "плод-зеленчук-и-маслини" };
            var driver = new ChromeDriver("./../../../../");
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
            foreach (var cat in Cats)
            {
                driver.Navigate().GoToUrl(string.Format(UrlFormat, cat));

                Task.Delay(TimeSpan.FromSeconds(5)).Wait();

                while (true)
                {
                    var products = driver.FindElements(By.ClassName("product-item"));

                    foreach (var prod in products)
                    {
                        productUrls.AppendLine(prod.FindElements(By.TagName("a"))[0].GetAttribute("href"));
                    }

                    try
                    {
                        driver.FindElement(By.ClassName("next-page")).FindElement(By.TagName("a")).Click();

                        Task.Delay(TimeSpan.FromSeconds(5)).Wait();
                    }
                    catch
                    {
                        break;
                    }
                }

            }

            if (!File.Exists("result.txt"))
            {
                File.Create("result.txt");
            }

            File.WriteAllText("result.txt", productUrls.ToString());
        }

        class Product
        {
            public Product(string name, string desc, string image, string brand, string cats)
            {
                Name = name;
                Desc = desc;
                Image = image;
                Brand = brand;
                Cats = cats;
            }

            public string Name { get; set; }

            public string Desc { get; set; }

            public string Image { get; set; }

            public string Brand { get; set; }

            public string Cats { get; set; }
        }
    }
}
