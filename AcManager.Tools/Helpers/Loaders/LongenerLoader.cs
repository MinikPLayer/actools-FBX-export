using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FirstFloor.ModernUI.Helpers;

namespace AcManager.Tools.Helpers.Loaders {
    internal class LongenerLoader : RedirectingLoader {
        public static bool IsFacebookWrapped(string url) => Regex.IsMatch(url,
                @"^https?://(?:www\.)?(?:l\.facebook\.com/l\.php|facebook\.com/flx/warn/)", RegexOptions.IgnoreCase);

        public static bool Test(string url) => IsFacebookWrapped(url) || Regex.IsMatch(url,
                @"^https?://(?:www\.)?(?:goo\.gl|bit\.ly|is\.gd|tinyurl\.com|turl\.ca|2\.gp)/", RegexOptions.IgnoreCase);

        public LongenerLoader(string url) : base(url) { }

        protected override Task<string> GetRedirectOverrideAsync(string url, CookieAwareWebClient client, CancellationToken cancellation) {
            if (IsFacebookWrapped(url)) {
                return Task.FromResult(new Uri(url, UriKind.RelativeOrAbsolute).GetQueryParam("u"));
            }

            return client.GetFinalRedirectAsync(url);
        }
    }
}