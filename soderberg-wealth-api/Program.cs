using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure JWT Auth
var jwtSecretKey = "SoderbergPartnersSecretKeyForSdetSimulators2026!#*";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "soderberg-wealth-issuer",
        ValidAudience = "soderberg-wealth-clients",
        IssuerSigningKey = signingKey,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdvisorOnly", policy => policy.RequireRole("Advisor"));
    options.AddPolicy("AdminOrAdvisor", policy => policy.RequireRole("Advisor", "Admin"));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Söderberg & Partners Pension & Wealth Core API",
        Version = "v1",
        Description = "Realistic enterprise backend for SDET Karate testing simulator"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Söderberg Wealth Core v1");
    c.RoutePrefix = string.Empty;
});

app.UseAuthentication();
app.UseAuthorization();

// -----------------------------------------------------------------------------------------
// IN-MEMORY DATA STORAGE
// -----------------------------------------------------------------------------------------
var clients = new Dictionary<string, Client>();
var portfolios = new Dictionary<string, Portfolio>();
var pensionFunds = new List<PensionFund>
{
    new("SE0001", "Nordic Growth Pension", "Equity", 145.20m, "SEK", 4),
    new("SE0002", "Söderberg Sustainable Horizon", "Balanced", 112.50m, "SEK", 3),
    new("SE0003", "Scandic Sovereign Bond Index", "FixedIncome", 98.75m, "SEK", 1),
    new("SE0004", "Global Tech Pension Alpha", "Equity", 210.80m, "USD", 5)
};
var orders = new Dictionary<string, AsyncOrder>();

// Pre-seed an initial client
var sampleClient = new Client(
    Id: "CLI-9001",
    NationalId: "19850412-1234",
    FullName: "Astrid Lindgren",
    Email: "astrid.lindgren@soderberg.se",
    RiskProfile: "Balanced",
    Status: "ACTIVE",
    CreatedAt: DateTime.UtcNow
);
clients[sampleClient.Id] = sampleClient;

// -----------------------------------------------------------------------------------------
// 1. AUTHENTICATION & TOKEN REFRESH (OAuth2 Resource Owner Password / Client Credentials)
// -----------------------------------------------------------------------------------------
app.MapPost("/api/v1/auth/token", (TokenRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "invalid_request", message = "Username and password required" });
    }

    string role = req.Username.ToLower() switch
    {
        "advisor" when req.Password == "NordicPass2026!" => "Advisor",
        "admin" when req.Password == "RootAdmin2026!" => "Admin",
        "customer" when req.Password == "Customer123!" => "Customer",
        _ => string.Empty
    };

    if (string.IsNullOrEmpty(role))
    {
        return Results.Json(new { error = "invalid_credentials", message = "Bad credentials" }, statusCode: 401);
    }

    var expiresMinutes = req.TtlMinutes.GetValueOrDefault(15);
    var claims = new[]
    {
        new Claim(ClaimTypes.Name, req.Username),
        new Claim(ClaimTypes.Role, role),
        new Claim("scope", "wealth:read wealth:write pension:transact")
    };

    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(claims),
        Expires = DateTime.UtcNow.AddMinutes(expiresMinutes),
        Issuer = "soderberg-wealth-issuer",
        Audience = "soderberg-wealth-clients",
        SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256Signature)
    };

    var tokenHandler = new JwtSecurityTokenHandler();
    var token = tokenHandler.CreateToken(tokenDescriptor);
    var jwtString = tokenHandler.WriteToken(token);

    return Results.Ok(new
    {
        access_token = jwtString,
        token_type = "Bearer",
        expires_in = expiresMinutes * 60,
        role = role
    });
}).WithTags("Authentication");

// -----------------------------------------------------------------------------------------
// 2. FUNDS & MARKET QUOTES (Public & Searchable)
// -----------------------------------------------------------------------------------------
app.MapGet("/api/v1/funds", (string? assetClass, string? currency) =>
{
    var query = pensionFunds.AsEnumerable();
    if (!string.IsNullOrEmpty(assetClass))
        query = query.Where(f => f.AssetClass.Equals(assetClass, StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrEmpty(currency))
        query = query.Where(f => f.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase));

    return Results.Ok(new
    {
        total = query.Count(),
        items = query.ToList()
    });
}).WithTags("Market Funds");

app.MapGet("/api/v1/funds/{isin}", (string isin) =>
{
    var fund = pensionFunds.FirstOrDefault(f => f.Isin.Equals(isin, StringComparison.OrdinalIgnoreCase));
    return fund is not null ? Results.Ok(fund) : Results.NotFound(new { error = "fund_not_found", isin });
}).WithTags("Market Funds");

// -----------------------------------------------------------------------------------------
// 3. CLIENTS & KYC ONBOARDING (Protected - Advisor only)
// -----------------------------------------------------------------------------------------
app.MapGet("/api/v1/clients", [Authorize] () => Results.Ok(clients.Values)).WithTags("Clients");

app.MapGet("/api/v1/clients/{id}", [Authorize] (string id) =>
{
    return clients.TryGetValue(id, out var client)
        ? Results.Ok(client)
        : Results.NotFound(new { error = "client_not_found", id });
}).WithTags("Clients");

app.MapPost("/api/v1/clients", [Authorize(Roles = "Advisor,Admin")] (CreateClientRequest req) =>
{
    // Nordic Personnummer format validation (YYYYMMDD-XXXX)
    if (!Regex.IsMatch(req.NationalId, @"^\d{8}-\d{4}$"))
    {
        return Results.UnprocessableEntity(new
        {
            error = "invalid_national_id",
            message = "NationalId must match Swedish/Nordic format YYYYMMDD-XXXX (e.g. 19880520-4321)"
        });
    }

    if (clients.Values.Any(c => c.NationalId == req.NationalId))
    {
        return Results.Conflict(new { error = "client_already_exists", message = "NationalId registered" });
    }

    var validProfiles = new[] { "Conservative", "Balanced", "Aggressive" };
    if (!validProfiles.Contains(req.RiskProfile, StringComparer.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            error = "invalid_risk_profile",
            allowed = validProfiles
        });
    }

    var id = $"CLI-{Random.Shared.Next(10000, 99999)}";
    var client = new Client(id, req.NationalId, req.FullName, req.Email, req.RiskProfile, "ACTIVE", DateTime.UtcNow);
    clients[id] = client;

    return Results.Created($"/api/v1/clients/{id}", client);
}).WithTags("Clients");

// -----------------------------------------------------------------------------------------
// 4. PORTFOLIOS & ASSET ALLOCATION (Must sum strictly 100%)
// -----------------------------------------------------------------------------------------
app.MapPost("/api/v1/clients/{clientId}/portfolios", [Authorize] (string clientId, CreatePortfolioRequest req) =>
{
    if (!clients.ContainsKey(clientId))
    {
        return Results.NotFound(new { error = "client_not_found", clientId });
    }

    if (req.Allocations == null || req.Allocations.Count == 0)
    {
        return Results.BadRequest(new { error = "empty_allocation", message = "At least 1 fund allocation required" });
    }

    var totalWeight = req.Allocations.Sum(a => a.Percentage);
    if (totalWeight != 100.0m)
    {
        return Results.UnprocessableEntity(new
        {
            error = "invalid_allocation_sum",
            message = $"Fund allocation percentages must sum exactly to 100.0. Current sum is: {totalWeight}"
        });
    }

    // Verify all ISINs exist
    foreach (var alloc in req.Allocations)
    {
        if (!pensionFunds.Any(f => f.Isin == alloc.Isin))
        {
            return Results.BadRequest(new { error = "unknown_isin", isin = alloc.Isin });
        }
    }

    var portfolioId = $"PTF-{Random.Shared.Next(20000, 89999)}";
    var portfolio = new Portfolio(portfolioId, clientId, req.Name, req.Allocations, 0.0m, "SEK", DateTime.UtcNow);
    portfolios[portfolioId] = portfolio;

    return Results.Created($"/api/v1/portfolios/{portfolioId}", portfolio);
}).WithTags("Portfolios");

app.MapGet("/api/v1/portfolios/{id}", [Authorize] (string id) =>
{
    return portfolios.TryGetValue(id, out var ptf)
        ? Results.Ok(ptf)
        : Results.NotFound(new { error = "portfolio_not_found", id });
}).WithTags("Portfolios");

// -----------------------------------------------------------------------------------------
// 5. CONTRIBUTIONS & TAX WITHHOLDING (Actuarial Calculation)
// -----------------------------------------------------------------------------------------
app.MapPost("/api/v1/portfolios/{portfolioId}/contributions", [Authorize] (string portfolioId, ContributionRequest req) =>
{
    if (!portfolios.TryGetValue(portfolioId, out var ptf))
    {
        return Results.NotFound(new { error = "portfolio_not_found", portfolioId });
    }

    if (req.GrossAmount <= 0)
    {
        return Results.BadRequest(new { error = "invalid_amount", message = "GrossAmount must be > 0" });
    }

    // Nordic Pension standard tax deduction: 15% upfront withholding for private individual plans
    decimal taxRate = req.PlanType.Equals("Corporate", StringComparison.OrdinalIgnoreCase) ? 0.05m : 0.15m;
    decimal taxWithheld = Math.Round(req.GrossAmount * taxRate, 2);
    decimal netInvested = req.GrossAmount - taxWithheld;

    var updatedPortfolio = ptf with { TotalBalance = ptf.TotalBalance + netInvested };
    portfolios[portfolioId] = updatedPortfolio;

    return Results.Ok(new
    {
        transactionId = $"TX-{Guid.NewGuid().ToString()[..8].ToUpper()}",
        portfolioId = portfolioId,
        grossAmount = req.GrossAmount,
        taxRate = taxRate,
        taxWithheld = taxWithheld,
        netInvested = netInvested,
        newBalance = updatedPortfolio.TotalBalance,
        currency = ptf.Currency,
        executedAt = DateTime.UtcNow
    });
}).WithTags("Contributions & Taxes");

// -----------------------------------------------------------------------------------------
// 6. ASYNC ORDERS & POLLING (202 Accepted -> Polling -> 200 Completed)
// -----------------------------------------------------------------------------------------
app.MapPost("/api/v1/orders/rebalance", [Authorize] (RebalanceOrderRequest req) =>
{
    if (!portfolios.ContainsKey(req.PortfolioId))
    {
        return Results.NotFound(new { error = "portfolio_not_found", req.PortfolioId });
    }

    var orderId = $"ORD-{Guid.NewGuid().ToString()[..8].ToUpper()}";
    var order = new AsyncOrder(orderId, req.PortfolioId, "PROCESSING", DateTime.UtcNow, null);
    orders[orderId] = order;

    return Results.Accepted($"/api/v1/orders/{orderId}/status", new
    {
        orderId = orderId,
        status = "PROCESSING",
        estimatedCompletionSeconds = 4,
        checkStatusUrl = $"/api/v1/orders/{orderId}/status"
    });
}).WithTags("Async Orders");

app.MapGet("/api/v1/orders/{orderId}/status", [Authorize] (string orderId) =>
{
    if (!orders.TryGetValue(orderId, out var order))
    {
        return Results.NotFound(new { error = "order_not_found", orderId });
    }

    // Simulate async transition: after 3 seconds becomes COMPLETED
    if (order.Status == "PROCESSING" && (DateTime.UtcNow - order.CreatedAt).TotalSeconds > 3)
    {
        order = order with { Status = "COMPLETED", CompletedAt = DateTime.UtcNow };
        orders[orderId] = order;
    }

    return Results.Ok(order);
}).WithTags("Async Orders");

// -----------------------------------------------------------------------------------------
// 7. CRYPTOGRAPHIC SIGNATURE / JAVA INTEROP ENDPOINT
// Requires Header X-Signature: HMACSHA256(BodyJson, "SoderbergSharedSecret2026")
// -----------------------------------------------------------------------------------------
app.MapPost("/api/v1/pension/transfers", [Authorize] async (HttpRequest request) =>
{
    if (!request.Headers.TryGetValue("X-Signature", out var signatureHeader))
    {
        return Results.Json(new
        {
            error = "missing_signature",
            message = "Header 'X-Signature' is required for capital transfers"
        }, statusCode: 403);
    }

    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    // Verify HMAC-SHA256 with shared secret
    var secret = "SoderbergSharedSecret2026";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var computedHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLower();

    if (!computedHash.Equals(signatureHeader.ToString().ToLower(), StringComparison.OrdinalIgnoreCase))
    {
        return Results.Json(new
        {
            error = "invalid_signature",
            expectedHashLength = computedHash.Length,
            message = "Signature does not match payload content"
        }, statusCode: 401);
    }

    return Results.Ok(new
    {
        status = "APPROVED",
        transferRef = $"TRF-{Guid.NewGuid().ToString()[..8].ToUpper()}",
        verifiedAt = DateTime.UtcNow
    });
}).WithTags("Secure Transfers");

app.Run("http://localhost:5125");

// -----------------------------------------------------------------------------------------
// RECORDS & DTOS
// -----------------------------------------------------------------------------------------
public record TokenRequest(string Username, string Password, int? TtlMinutes);
public record PensionFund(string Isin, string Name, string AssetClass, decimal Nav, string Currency, int RiskGrade);
public record Client(string Id, string NationalId, string FullName, string Email, string RiskProfile, string Status, DateTime CreatedAt);
public record CreateClientRequest(string NationalId, string FullName, string Email, string RiskProfile);
public record FundAllocation(string Isin, decimal Percentage);
public record Portfolio(string Id, string ClientId, string Name, List<FundAllocation> Allocations, decimal TotalBalance, string Currency, DateTime CreatedAt);
public record CreatePortfolioRequest(string Name, List<FundAllocation> Allocations);
public record ContributionRequest(decimal GrossAmount, string PlanType);
public record RebalanceOrderRequest(string PortfolioId, string Strategy);
public record AsyncOrder(string OrderId, string PortfolioId, string Status, DateTime CreatedAt, DateTime? CompletedAt);
