using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TimeSync.Toolkit
{
    public class BrowserLogin
    {
        public static CookieContainer GetLoginCookies(string siteUrl, System.Drawing.Icon icon = null, bool scriptErrorsSuppressed = true, Uri loginRequestUri = null)
        {
            var authCookiesContainer = new CookieContainer();
            var siteUri = new Uri(siteUrl);
            
            Exception threadException = null;

            var thread = new Thread(() =>
            {
                try
                {
                    var form = new Form();
                    if (icon != null)
                    {
                        form.Icon = icon;
                    }
                    CoreWebView2Environment.SetLoaderDllFolderPath(Path.GetDirectoryName(typeof(BrowserLogin).Assembly.Location));
                    var env = CoreWebView2Environment.CreateAsync(
                        options: new CoreWebView2EnvironmentOptions(allowSingleSignOnUsingOSPrimaryAccount: true),
                        userDataFolder: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"TimeSync\WebView2UserData")
                    ).Result;
                    var browser = new WebView2();

                    form.SuspendLayout();
                    form.Width = 900;
                    form.Height = 500;
                    form.Text = $"Log in to {siteUrl}";
                    form.Controls.Add(browser);
                    form.WindowState = FormWindowState.Minimized;
                    browser.Top = 0;
                    browser.Left = 0;
                    browser.Width = form.Width;
                    browser.Height = form.Height;
                    browser.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
                    form.ResumeLayout(true);

                    form.Load += (sender, args) =>
                    {
                        browser.EnsureCoreWebView2Async(env);
                        browser.Source = loginRequestUri ?? siteUri;
                    };

                    browser.NavigationCompleted += async (sender, args) =>
                    {
                        if ((loginRequestUri ?? siteUri).Host.Equals(browser.Source.Host))
                        {
                            var cookies = await browser.CoreWebView2.CookieManager.GetCookiesAsync((loginRequestUri ?? siteUri).AbsoluteUri);

                            var fedAuthCookies = cookies.Where(c => c.Name.StartsWith("FedAuth", StringComparison.CurrentCultureIgnoreCase) || c.Name.StartsWith("rtFa", StringComparison.CurrentCultureIgnoreCase)).ToList();
                            var edgeAccessCookies = cookies.Where(c => c.Name.StartsWith("EdgeAccessCookie", StringComparison.CurrentCultureIgnoreCase)).ToList();

                            // Get FedAuth and rtFa cookies issued by ADFS when accessing claims aware applications.
                            // - or get the EdgeAccessCookie issued by the Web Application Proxy (WAP) when accessing non-claims aware applications (Kerberos).
                            var authCookies = fedAuthCookies.Count > 0 ? fedAuthCookies : edgeAccessCookies;
                            if (authCookies.Count > 0)
                            {
                                // Set the authentication cookies both on the SharePoint Online Admin as well as on the SharePoint Online domains to allow for APIs on both domains to be used
                                var adminSiteUri = siteUri.Authority.Contains(".sharepoint.com") ? new Uri(siteUri.Scheme + "://" + siteUri.Authority.Replace(".sharepoint.com", "-admin.sharepoint.com")) : null;
                                foreach (var cookie in authCookies)
                                {
                                    var netCookie = cookie.ToSystemNetCookie();
                                    authCookiesContainer.Add(siteUri, netCookie);
                                    if (adminSiteUri != null)
                                        authCookiesContainer.Add(adminSiteUri, netCookie);
                                }
                                form.Close();
                            }
                            else
                            {
                                form.WindowState = FormWindowState.Normal;
                            }
                        }
                    };

                    form.Focus();
                    form.ShowDialog();                
                    browser.Dispose();
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (threadException != null)
                throw new Exception("Failed to show browser login", threadException);

            return authCookiesContainer;
        }
    }
}
