<div align=center>
<h1>TooManyRequestsHandler</h1>

[![Nuget](https://img.shields.io/nuget/v/TooManyRequestsHandler?style=flat-square)](https://www.nuget.org/packages/TooManyRequestsHandler/)
[![Downloads](https://img.shields.io/nuget/dt/TooManyRequestsHandler.svg?style=flat-square)](https://www.nuget.org/packages/TooManyRequestsHandler/)
[![Build](https://img.shields.io/github/actions/workflow/status/baltermia/too-many-requests-handler/build.yml?style=flat-square)](https://github.com/baltermia/too-many-requests-handler/actions/workflows/build.yml)

A simple HttpClientHandler which handles HTTP 429 (TooManyRequest) and also allows parallel pausing.
</div>

## How to Use

### Using `IHttpClientFactory`

```csharp
IHost host = Host.CreateDefaultBuilder()

                .ConfigureAppConfiguration( /* Configure your app-config */)

                .ConfigureLogging(/* Configure your logging */)

                .ConfigureServices(services =>
                    services
                        .AddHttpClient()

                        // Here is where you add the handler
                        .ConfigurePrimaryHttpMessageHandler(_ => new TooManyRequestsHandler
                        {
                            // Any settings you want (since HttpClientHandler is derived from)
                        })
                        
                        .ConfigureHttpClient((_, client) => 
                        {
                            client.DefaultRequestHeaders.Accept.Add(new("application/json"));
                            // might also add other settings
                        }))
                .Services
                .Build();
```


### In `HttpClient`-Constructor

Simply provide the `TooManyRequestsHandler` when constructing a new `HttpClient`:
```csharp
TooManyRequestsHandler handler = new 
{
    // Any settings you want (since HttpClientHandler is derived from)
};

HttpClient client = new(handler);
```

## Compatibility

The library supports the following .NET versions:
- .NET 5+
- .NET Standard 2.1+

All other versions `StatusCode.TooManyRequests` is not avaliable, therefore the library will not be supported for those versions.

## Inconveniences

> This explains why you might get "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing." exception messages.

### Fix

To fix the problem, simply set the `HttpClient.Timeout` property to a greated value or infinite ([`TimeSpan.InfiniteTimeSpan`](https://learn.microsoft.com/en-us/dotnet/api/system.threading.timeout.infinitetimespan), or for < .NET 7 `TimeSpan.FromMilliseconds(-1)`;


```csharp
// For >= .NET 7
TimeSpan timeout = TimeSpan.InfiniteTimeSpan;

// For < .NET 7
TimeSpan timeout = TimeSpan.FromMilliseconds(-1);

// Using IHttpClientFactory
.ConfigureHttpClient((_, client) => 
{
    client.Timeout = timeout;
})

// Or using constructor
HttpClient client = new(handler)
{
    Timeout = timeout
};
```

### Explanation

The `HttpClient` class holds a `Timeout` property, which by default is set to 100 seconds. (More about it [here](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout#system-net-http-httpclient-timeout))

That Timeout has nothing to do with requests directly, but rather with how long the `HttpClient`s underlying `HttpMessageHandler` takes. And since that handler in our case asynchronously waits until the time in the `HttpResponseMessage.Headers.RetryAfter.Date` is reached, the time for a request will rise with each retry.

It's a very unfortunate problem and I haven't found a good fix for it yet (not setting the Timeout to infinite), but I would be glad if someone could help me fix it!
