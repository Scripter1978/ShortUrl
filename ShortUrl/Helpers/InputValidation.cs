using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Net;

namespace ShortUrl.Helpers;

public static class InputValidation
{
    // URL slugs (already implemented but now centralized)
    public static bool IsValidSlug(string slug) => 
        !string.IsNullOrWhiteSpace(slug) && Regex.IsMatch(slug, @"^[a-zA-Z0-9\-_]{1,50}$");
        
    // Basic URL validation - checks if URL is well-formed
    public static bool IsValidUrl(string url) =>
        !string.IsNullOrWhiteSpace(url) && 
        Uri.TryCreate(url, UriKind.Absolute, out var uriResult) && 
        (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    
    // Enhanced URL validation with security checks
    public static bool IsSecureUrl(string url, List<string>? allowedDomains = null)
    {
        // First check if it's a valid URL
        if (!IsValidUrl(url))
            return false;
            
        // Parse the URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
            
        // Ensure only HTTP or HTTPS protocols are used
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;
            
        // Prevent localhost and private IP addresses in production
        if (IsLocalhost(uri.Host) || IsPrivateIpAddress(uri.Host))
            return false;
            
        // If allowed domains are specified, check against the list
        if (allowedDomains != null && allowedDomains.Any())
        {
            // Check if the domain is in the allowed list
            bool isAllowed = false;
            foreach (var domain in allowedDomains)
            {
                if (uri.Host.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
                {
                    isAllowed = true;
                    break;
                }
            }
            
            if (!isAllowed)
                return false;
        }
        
        return true;
    }
    
    // Check if hostname is localhost
    private static bool IsLocalhost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || 
               host.Equals("127.0.0.1") || 
               host.Equals("::1");
    }
    
    // Check if IP address is in private range
    private static bool IsPrivateIpAddress(string host)
    {
        // Check if it's an IP address
        if (!System.Net.IPAddress.TryParse(host, out var ipAddress))
            return false;
            
        // Convert to bytes for easy comparison
        byte[] bytes = ipAddress.GetAddressBytes();
        
        // Check for IPv4 private ranges
        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;
                
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;
                
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
        }
        
        // For IPv6, check if it's a link-local address
        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ipAddress.IsIPv6LinkLocal)
                return true;
        }
        
        return false;
    }
        
    // Email validation
    public static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) && 
        Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        
    // Password validation - enforce minimum security requirements
    public static bool IsValidPassword(string password) =>
        !string.IsNullOrWhiteSpace(password) && 
        password.Length >= 6 &&
        Regex.IsMatch(password, @"[A-Z]") && // At least one uppercase
        Regex.IsMatch(password, @"[a-z]") && // At least one lowercase
        Regex.IsMatch(password, @"[0-9]");   // At least one number
        
    // General text sanitization (for fields like names, titles, etc.)
    public static string SanitizeInput(string input) =>
        string.IsNullOrWhiteSpace(input) ? string.Empty : 
            HtmlEncoder.Default.Encode(input.Trim());
            
    // Sanitize HTML content for specific fields where some HTML is allowed
    // but we need to prevent XSS attacks
    public static string SanitizeHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;
            
        // Use a whitelist approach - only allow specific safe HTML tags
        // First, encode everything to prevent any scripts
        var encodedHtml = HtmlEncoder.Default.Encode(html);
        
        // Then selectively allow only safe HTML tags
        var allowedTags = new[] { "b", "i", "u", "p", "br", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "a", "strong", "em" };
        
        // Replace encoded versions of allowed tags with their actual tags
        foreach (var tag in allowedTags)
        {
            // Opening tags
            encodedHtml = Regex.Replace(
                encodedHtml, 
                HtmlEncoder.Default.Encode($"<{tag}>"), 
                $"<{tag}>", 
                RegexOptions.IgnoreCase
            );
            
            // Closing tags
            encodedHtml = Regex.Replace(
                encodedHtml, 
                HtmlEncoder.Default.Encode($"</{tag}>"), 
                $"</{tag}>", 
                RegexOptions.IgnoreCase
            );
            
            // For anchor tags, allow only safe attributes (href, title, target)
            if (tag == "a")
            {
                // Replace encoded versions of href attribute with safe versions
                encodedHtml = Regex.Replace(
                    encodedHtml,
                    HtmlEncoder.Default.Encode("<a href=\"") + "(.*?)" + HtmlEncoder.Default.Encode("\">"),
                    match => {
                        var url = WebUtility.HtmlDecode(match.Groups[1].Value);
                        // Only allow http and https URLs
                        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && 
                            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                            return $"<a href=\"{url}\">";
                        return "<a href=\"#\">";
                    },
                    RegexOptions.IgnoreCase
                );
                
                // Handle target attribute
                encodedHtml = Regex.Replace(
                    encodedHtml,
                    HtmlEncoder.Default.Encode(" target=\"") + "(.*?)" + HtmlEncoder.Default.Encode("\""),
                    match => {
                        var target = WebUtility.HtmlDecode(match.Groups[1].Value);
                        // Only allow _blank, _self, _parent, _top
                        if (new[] { "_blank", "_self", "_parent", "_top" }.Contains(target))
                            return $" target=\"{target}\"";
                        return "";
                    },
                    RegexOptions.IgnoreCase
                );
                
                // Handle title attribute
                encodedHtml = Regex.Replace(
                    encodedHtml,
                    HtmlEncoder.Default.Encode(" title=\"") + "(.*?)" + HtmlEncoder.Default.Encode("\""),
                    match => {
                        var title = WebUtility.HtmlDecode(match.Groups[1].Value);
                        return $" title=\"{HtmlEncoder.Default.Encode(title)}\"";
                    },
                    RegexOptions.IgnoreCase
                );
            }
        }
        
        return encodedHtml;
    }
}