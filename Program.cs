using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("JewelleryDb")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=JewelleryDb;Trusted_Connection=True;TrustServerCertificate=True;";
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured. Set it in appsettings.json or as an environment variable.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SohamJewellers";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SohamJewellersUsers";

if (jwtKey.StartsWith("REPLACE_THIS"))
    throw new InvalidOperationException("Jwt:Key is still set to the placeholder value. Please set a real secret key in appsettings.json or as an environment variable Jwt__Key.");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024;
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();
var uploadsRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads", "products");
Directory.CreateDirectory(uploadsRoot);

try
{
    EnsureDatabase(connectionString);
}
catch (Exception ex)
{
    Console.WriteLine($"Database startup error: {ex.Message}");
    throw;
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    message = "Soham Jewellers backend is running"
}));

app.MapPost("/register-owner", (RegisterRequest request) =>
{
    var validationError = ValidateRegisterRequest(request);
    if (validationError is not null)
        return Results.BadRequest(new { message = validationError });

    using var connection = new SqlConnection(connectionString);
    connection.Open();

    if (EmailExists(connection, "Owners", request.Email))
        return Results.BadRequest(new { message = "Owner email already exists." });

    var hashResult = PasswordHelper.HashPassword(request.Password);

    using var command = new SqlCommand(@"
        INSERT INTO Owners (Name, Email, PasswordHash, PasswordSalt, Phone, CreatedAt)
        VALUES (@Name, @Email, @PasswordHash, @PasswordSalt, @Phone, GETDATE())", connection);

    command.Parameters.AddWithValue("@Name", request.Name.Trim());
    command.Parameters.AddWithValue("@Email", request.Email.Trim().ToLower());
    command.Parameters.AddWithValue("@PasswordHash", hashResult.Hash);
    command.Parameters.AddWithValue("@PasswordSalt", hashResult.Salt);
    command.Parameters.AddWithValue("@Phone", string.IsNullOrWhiteSpace(request.Phone) ? (object)DBNull.Value : request.Phone.Trim());

    command.ExecuteNonQuery();

    return Results.Ok(new { message = "Owner registered successfully." });
});

app.MapPost("/register-user", (RegisterRequest request) =>
{
    var validationError = ValidateRegisterRequest(request);
    if (validationError is not null)
        return Results.BadRequest(new { message = validationError });

    using var connection = new SqlConnection(connectionString);
    connection.Open();

    if (EmailExists(connection, "Users", request.Email))
        return Results.BadRequest(new { message = "User email already exists." });

    var hashResult = PasswordHelper.HashPassword(request.Password);

    using var command = new SqlCommand(@"
        INSERT INTO Users (FullName, Email, PasswordHash, PasswordSalt, Phone, CreatedAt)
        VALUES (@FullName, @Email, @PasswordHash, @PasswordSalt, @Phone, GETDATE())", connection);

    command.Parameters.AddWithValue("@FullName", request.Name.Trim());
    command.Parameters.AddWithValue("@Email", request.Email.Trim().ToLower());
    command.Parameters.AddWithValue("@PasswordHash", hashResult.Hash);
    command.Parameters.AddWithValue("@PasswordSalt", hashResult.Salt);
    command.Parameters.AddWithValue("@Phone", string.IsNullOrWhiteSpace(request.Phone) ? (object)DBNull.Value : request.Phone.Trim());

    command.ExecuteNonQuery();

    return Results.Ok(new { message = "User registered successfully." });
});

app.MapPost("/login", (LoginRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { message = "Email and password are required." });

    using var connection = new SqlConnection(connectionString);
    connection.Open();

    var owner = FindAccount(connection, "Owners", "Name", request.Email);
    if (owner is not null)
    {
        var valid = PasswordHelper.VerifyPassword(request.Password, owner.PasswordHash, owner.PasswordSalt);
        if (!valid)
            return Results.BadRequest(new { message = "Invalid email or password." });

        var token = GenerateJwtToken(owner.Id, owner.Name, owner.Email, "Owner", jwtKey, jwtIssuer, jwtAudience);
        return Results.Ok(new
        {
            message = "Login successful.",
            token,
            role = "Owner",
            name = owner.Name,
            email = owner.Email
        });
    }

    var user = FindAccount(connection, "Users", "FullName", request.Email);
    if (user is not null)
    {
        var valid = PasswordHelper.VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt);
        if (!valid)
            return Results.BadRequest(new { message = "Invalid email or password." });

        var token = GenerateJwtToken(user.Id, user.Name, user.Email, "User", jwtKey, jwtIssuer, jwtAudience);
        return Results.Ok(new
        {
            message = "Login successful.",
            token,
            role = "User",
            name = user.Name,
            email = user.Email
        });
    }

    return Results.BadRequest(new { message = "Invalid email or password." });
});

app.MapPost("/products", async (HttpRequest httpRequest, ClaimsPrincipal user) =>
{
    if (!user.Identity?.IsAuthenticated ?? true)
        return Results.Unauthorized();

    if (!user.IsInRole("Owner"))
        return Results.Forbid();

    if (!httpRequest.HasFormContentType)
        return Results.BadRequest(new { message = "Use multipart/form-data to upload product details and image." });

    var form = await httpRequest.ReadFormAsync();

    var request = new ProductFormRequest(
        form["name"].ToString(),
        form["collection"].ToString(),
        form["weight"].ToString(),
        form["purity"].ToString(),
        form["makingCharge"].ToString(),
        form["price"].ToString(),
        form.Files["image"]
    );

    var validationError = ValidateProductFormRequest(request, out var weight, out var makingCharge, out var price);
    if (validationError is not null)
        return Results.BadRequest(new { message = validationError });

    string? savedImagePath = null;
    if (request.Image is not null && request.Image.Length > 0)
    {
        var imageValidationError = ValidateImage(request.Image);
        if (imageValidationError is not null)
            return Results.BadRequest(new { message = imageValidationError });

        var extension = Path.GetExtension(request.Image.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid()}{extension}";
        var physicalPath = Path.Combine(uploadsRoot, fileName);

        await using var stream = File.Create(physicalPath);
        await request.Image.CopyToAsync(stream);

        savedImagePath = $"/uploads/products/{fileName}";
    }

    using var connection = new SqlConnection(connectionString);
    connection.Open();

    using var command = new SqlCommand(@"
        INSERT INTO Products (Name, Collection, Weight, Purity, MakingCharge, Price, PhotoPath, CreatedAt)
        VALUES (@Name, @Collection, @Weight, @Purity, @MakingCharge, @Price, @PhotoPath, GETDATE());

        SELECT SCOPE_IDENTITY();", connection);

    command.Parameters.AddWithValue("@Name", request.Name.Trim());
    command.Parameters.AddWithValue("@Collection", request.Collection.Trim());
    command.Parameters.AddWithValue("@Weight", weight);
    command.Parameters.AddWithValue("@Purity", request.Purity.Trim());
    command.Parameters.AddWithValue("@MakingCharge", makingCharge);
    command.Parameters.AddWithValue("@Price", price);
    command.Parameters.AddWithValue("@PhotoPath", string.IsNullOrWhiteSpace(savedImagePath) ? (object)DBNull.Value : savedImagePath);

    var newId = Convert.ToInt32(command.ExecuteScalar());

    return Results.Ok(new
    {
        message = "Product added successfully.",
        productId = newId,
        imageUrl = savedImagePath
    });
}).RequireAuthorization();

app.MapGet("/products", () =>
{
    var products = new List<ProductResponse>();

    using var connection = new SqlConnection(connectionString);
    connection.Open();

    using var command = new SqlCommand(@"
        SELECT Id, Name, Collection, Weight, Purity, MakingCharge, Price, PhotoPath, CreatedAt
        FROM Products
        ORDER BY Id DESC", connection);

    using var reader = command.ExecuteReader();

    while (reader.Read())
    {
        products.Add(new ProductResponse(
            Id: Convert.ToInt32(reader["Id"]),
            Name: reader["Name"].ToString() ?? "",
            Collection: reader["Collection"].ToString() ?? "",
            Weight: Convert.ToDecimal(reader["Weight"]),
            Purity: reader["Purity"].ToString() ?? "",
            MakingCharge: Convert.ToDecimal(reader["MakingCharge"]),
            Price: Convert.ToDecimal(reader["Price"]),
            PhotoPath: reader["PhotoPath"] == DBNull.Value ? null : reader["PhotoPath"].ToString(),
            CreatedAt: Convert.ToDateTime(reader["CreatedAt"])
        ));
    }

    return Results.Ok(products);
});

app.MapGet("/products/{id:int}", (int id) =>
{
    using var connection = new SqlConnection(connectionString);
    connection.Open();

    using var command = new SqlCommand(@"
        SELECT Id, Name, Collection, Weight, Purity, MakingCharge, Price, PhotoPath, CreatedAt
        FROM Products WHERE Id = @Id", connection);
    command.Parameters.AddWithValue("@Id", id);

    using var reader = command.ExecuteReader();
    if (!reader.Read())
        return Results.NotFound(new { message = "Product not found." });

    var product = new ProductResponse(
        Id: Convert.ToInt32(reader["Id"]),
        Name: reader["Name"].ToString() ?? "",
        Collection: reader["Collection"].ToString() ?? "",
        Weight: Convert.ToDecimal(reader["Weight"]),
        Purity: reader["Purity"].ToString() ?? "",
        MakingCharge: Convert.ToDecimal(reader["MakingCharge"]),
        Price: Convert.ToDecimal(reader["Price"]),
        PhotoPath: reader["PhotoPath"] == DBNull.Value ? null : reader["PhotoPath"].ToString(),
        CreatedAt: Convert.ToDateTime(reader["CreatedAt"])
    );

    return Results.Ok(product);
});

app.Run();

static void EnsureDatabase(string connectionString)
{
    using var masterConnection = new SqlConnection("Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;");
    masterConnection.Open();

    using (var createDbCommand = new SqlCommand(@"
        IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = 'JewelleryDb')
        BEGIN
            CREATE DATABASE JewelleryDb;
        END", masterConnection))
    {
        createDbCommand.ExecuteNonQuery();
    }

    using var dbConnection = new SqlConnection(connectionString);
    dbConnection.Open();

    using var command = new SqlCommand(@"
        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Owners')
        BEGIN
            CREATE TABLE Owners (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Name NVARCHAR(100) NOT NULL,
                Email NVARCHAR(100) NOT NULL UNIQUE,
                PasswordHash NVARCHAR(255) NOT NULL,
                PasswordSalt NVARCHAR(255) NOT NULL,
                Phone NVARCHAR(20) NULL,
                CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
            );
        END

        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users')
        BEGIN
            CREATE TABLE Users (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                FullName NVARCHAR(100) NOT NULL,
                Email NVARCHAR(100) NOT NULL UNIQUE,
                PasswordHash NVARCHAR(255) NOT NULL,
                PasswordSalt NVARCHAR(255) NOT NULL,
                Phone NVARCHAR(20) NULL,
                CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
            );
        END

        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Products')
        BEGIN
            CREATE TABLE Products (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Name NVARCHAR(100) NOT NULL,
                Collection NVARCHAR(100) NOT NULL,
                Weight DECIMAL(10,2) NOT NULL,
                Purity NVARCHAR(20) NOT NULL,
                MakingCharge DECIMAL(10,2) NOT NULL,
                Price DECIMAL(12,2) NOT NULL,
                PhotoPath NVARCHAR(255) NULL,
                CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
            );
        END", dbConnection);

    command.ExecuteNonQuery();
}

static string? ValidateRegisterRequest(RegisterRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return "Name is required.";

    if (string.IsNullOrWhiteSpace(request.Email))
        return "Email is required.";

    if (string.IsNullOrWhiteSpace(request.Password))
        return "Password is required.";

    if (request.Password.Length < 8)
        return "Password must be at least 8 characters.";

    if (!request.Email.Contains("@"))
        return "Valid email is required.";

    return null;
}

static string? ValidateProductFormRequest(ProductFormRequest request, out decimal weight, out decimal makingCharge, out decimal price)
{
    weight = 0;
    makingCharge = 0;
    price = 0;

    if (string.IsNullOrWhiteSpace(request.Name))
        return "Product name is required.";

    if (string.IsNullOrWhiteSpace(request.Collection))
        return "Collection is required.";

    if (!decimal.TryParse(request.Weight, out weight) || weight <= 0)
        return "Weight must be a valid number greater than zero.";

    if (string.IsNullOrWhiteSpace(request.Purity))
        return "Purity is required.";

    if (!decimal.TryParse(request.MakingCharge, out makingCharge) || makingCharge < 0)
        return "Making charge must be a valid number and cannot be negative.";

    if (!decimal.TryParse(request.Price, out price) || price <= 0)
        return "Price must be a valid number greater than zero.";

    return null;
}

static string? ValidateImage(IFormFile image)
{
    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
    var extension = Path.GetExtension(image.FileName).ToLowerInvariant();

    if (!allowedExtensions.Contains(extension))
        return "Only .jpg, .jpeg, .png, and .webp image files are allowed.";

    if (image.Length > 5 * 1024 * 1024)
        return "Image size must be 5 MB or less.";

    return null;
}

static bool EmailExists(SqlConnection connection, string tableName, string email)
{
    using var command = new SqlCommand($"SELECT COUNT(1) FROM {tableName} WHERE Email = @Email", connection);
    command.Parameters.AddWithValue("@Email", email.Trim().ToLower());
    return Convert.ToInt32(command.ExecuteScalar()) > 0;
}

static AccountRecord? FindAccount(SqlConnection connection, string tableName, string nameColumn, string email)
{
    using var command = new SqlCommand($@"
        SELECT TOP 1 Id, {nameColumn}, Email, PasswordHash, PasswordSalt
        FROM {tableName}
        WHERE Email = @Email", connection);

    command.Parameters.AddWithValue("@Email", email.Trim().ToLower());

    using var reader = command.ExecuteReader();
    if (!reader.Read())
        return null;

    return new AccountRecord(
        Id: Convert.ToInt32(reader["Id"]),
        Name: reader[nameColumn].ToString() ?? "",
        Email: reader["Email"].ToString() ?? "",
        PasswordHash: reader["PasswordHash"].ToString() ?? "",
        PasswordSalt: reader["PasswordSalt"].ToString() ?? ""
    );
}

static string GenerateJwtToken(int id, string name, string email, string role, string jwtKey, string jwtIssuer, string jwtAudience)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, id.ToString()),
        new Claim(ClaimTypes.Name, name),
        new Claim(ClaimTypes.Email, email),
        new Claim(ClaimTypes.Role, role)
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(12),
        signingCredentials: credentials
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
}

record RegisterRequest(string Name, string Email, string Password, string? Phone);
record LoginRequest(string Email, string Password);
record ProductFormRequest(string Name, string Collection, string Weight, string Purity, string MakingCharge, string Price, IFormFile? Image);
record ProductResponse(int Id, string Name, string Collection, decimal Weight, string Purity, decimal MakingCharge, decimal Price, string? PhotoPath, DateTime CreatedAt);
record AccountRecord(int Id, string Name, string Email, string PasswordHash, string PasswordSalt);

static class PasswordHelper
{
    public static PasswordHashResult HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        return new PasswordHashResult(
            Convert.ToBase64String(hashBytes),
            Convert.ToBase64String(saltBytes)
        );
    }

    public static bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        var saltBytes = Convert.FromBase64String(storedSalt);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            100_000,
            HashAlgorithmName.SHA256,
            32);
        var computedHash = Convert.ToBase64String(hashBytes);

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(storedHash),
            Convert.FromBase64String(computedHash)
        );
    }
}

record PasswordHashResult(string Hash, string Salt);
