﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace ShootR
{
    public class Startup
    {
        static long GuestID = 0;

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc()
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

            services.AddSignalR()
                .AddJsonProtocol(o =>
                {
                    o.PayloadSerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver();
                });

            services.AddSingleton<Game>();

            services.AddAuthorization();

            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Login";
                });

            services.AddDataProtection();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, Game game, IDataProtectionProvider provider)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseFileServer();

            app.UseAuthentication();

            app.Use((context, next) =>
            {
                var state = context.Request.Cookies["shootr.state"];

                if (state != null)
                {
                    if (!context.User.Identity.IsAuthenticated)
                    {
                        // Clear the state cookie.
                        context.Response.Cookies.Delete("shootr.state");
                    }
                    else
                    {
                        try
                        {
                            string decoded = HttpUtility.UrlDecode(state);
                            var rc = JsonConvert.DeserializeObject<RegisteredClient>(decoded);

                            if (rc.Identity == "Guest")
                            {
                                rc.DisplayName = "Guest" + Interlocked.Increment(ref GuestID);
                                rc.Identity = "Guest" + Guid.NewGuid().ToString();
                                rc.RegistrationID = null;
                                rc.Photo = "";
                            }
                            else
                            {
                                Byte[] encryptedIdentity = WebEncoders.Base64UrlDecode(rc.Identity);
                                var unprotectedIdentity = provider.CreateProtector("ShootR.Identity").Unprotect(encryptedIdentity);
                                rc.Identity = Encoding.UTF8.GetString(unprotectedIdentity);
                            }

                            rc.DisplayName = System.Net.WebUtility.HtmlEncode(rc.DisplayName);

                            game.RegistrationHandler.Register(rc);

                            SetState(rc, context, provider);
                        }
                        catch
                        {
                        }
                    }
                }
                else if (context.User.Identity.IsAuthenticated)
                {
                    //hack
                    var id = context.User.Claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value;
                    var name = context.User.Claims.Single(c => c.Type == ClaimTypes.Name).Value;
                    var role = context.User.Claims.Single(c => c.Type == ClaimTypes.Role).Value;
                    var rc = new RegisteredClient(null, id, name, "", role);
                    game.RegistrationHandler.Register(rc);

                    SetState(rc, context, provider); 
                }

                return next();
            });

            app.UseMvc();

            app.UseSignalR(routes =>
            {
                routes.MapHub<GameHub>("/Game");
            });
        }

        public static void SetState(RegisteredClient rc, HttpContext context, IDataProtectionProvider provider)
        {
            // Save the cookie state
            Byte[] identity = Encoding.UTF8.GetBytes(rc.Identity);
            Byte[] encrypted = provider.CreateProtector("ShootR.Identity").Protect(identity);
            var temp = new RegisteredClient(rc.RegistrationID, WebEncoders.Base64UrlEncode(encrypted), rc.DisplayName, rc.Photo, rc.Role);
            var state = JsonConvert.SerializeObject(temp);

            context.Response.Cookies.Append("shootr.state", state, new CookieOptions
            {
                Expires = DateTime.Now.AddDays(30)
            });
        }
    }
}
