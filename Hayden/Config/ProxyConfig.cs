using System.Collections.Generic;
using Hayden.Contract;

namespace Hayden.Config;

/// <summary>
/// Configuration object for the 4chan API
/// </summary>
public class ProxyConfig
{
	/// <summary>
	/// Configures if the local un-proxied connection should be used as an additional connection, outside the specified proxies.
	/// </summary>
	public bool EnableLocalConnection { get; set; } = true;

	/// <summary>
	/// Whether or not the DNS resolution should happen on your machine or through each proxy.
	/// </summary>
	public bool ResolveDnsLocally { get; set; } = false;

	/// <summary>
	/// <para>A list of proxy URIs for Hayden to use. Example proxy URLs:</para>
	/// <code>socks5://username:password@123.123.123.123:8080</code>
	/// </summary>
	public string[] Proxies { get; set; } = new string[0];
}