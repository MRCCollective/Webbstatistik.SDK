# Datadredge.SDK

Server-side pageview tracking SDK for [Datadredge analytics](https://datadredge.se).

## Installation

```bash
dotnet add package Datadredge.SDK
```

## Configuration

Add to your `Program.cs`:

```csharp
builder.Services.AddServerPageviewTracking(builder.Configuration);

var app = builder.Build();
app.UseServerPageviewTracking();
```

Add to your `appsettings.json`:

```json
{
  "Tracking:ServerPageviews": {
    "DatadredgeBaseUrl": "https://datadredge.se",
    "SiteKey": "your-site-key",
    "WebsiteId": "your-website-id"
  }
}
```

## Usage

The SDK automatically tracks pageviews for HTML responses. Events are queued and batched to the Datadredge analytics backend.
