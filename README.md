# Soham Jewellers – Backend API

A simple, secure ASP.NET Core minimal API backend for the Soham Jewellers website.  
Supports owner/user authentication, product management, and product image uploads.

---

## Requirements

| Tool | Version |
|------|---------|
| .NET SDK | 8, 9, or 10 |
| SQL Server | LocalDB (bundled with Visual Studio) **or** any SQL Server instance |

Download .NET SDK: <https://dotnet.microsoft.com/download>  
Download SQL Server Express + LocalDB: <https://www.microsoft.com/en-us/sql-server/sql-server-downloads>

---

## Configuration

Open `appsettings.json` and set a **strong JWT secret key** (at least 32 characters):

```json
{
  "ConnectionStrings": {
    "JewelleryDb": "Server=(localdb)\\MSSQLLocalDB;Database=JewelleryDb;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Jwt": {
    "Key": "REPLACE_THIS_WITH_A_LONG_RANDOM_SECRET_AT_LEAST_32_CHARS",
    "Issuer": "SohamJewellers",
    "Audience": "SohamJewellersUsers"
  }
}
```

> **Never commit a real JWT key to source control.** For production, set `Jwt__Key` as an environment variable or use a secret manager.

---

## Run the backend

```bash
# 1. Restore packages
dotnet restore

# 2. Run the app  (database tables are created automatically on first start)
dotnet run
```

The API starts at `http://localhost:5050`.  
Open `http://localhost:5050/swagger` in your browser to explore and test all endpoints interactively.

---

## API Endpoints

### Health check
```
GET /
```

### Register an owner (shop admin)
```
POST /register-owner
Content-Type: application/json

{
  "name": "Soham",
  "email": "owner@example.com",
  "password": "StrongPass123",
  "phone": "9999999999"
}
```

### Register a user (customer)
```
POST /register-user
Content-Type: application/json

{
  "name": "Rahul Patel",
  "email": "rahul@example.com",
  "password": "StrongPass123",
  "phone": "9876543210"
}
```

### Login (owner or user)
```
POST /login
Content-Type: application/json

{
  "email": "owner@example.com",
  "password": "StrongPass123"
}
```

Response includes a JWT `token` and `role` (`Owner` or `User`). Use the token for authenticated requests.

### Add a product with image upload *(Owner only)*
```
POST /products
Authorization: ******
Content-Type: multipart/form-data

Fields:
  name          – product name (required)
  collection    – collection name, e.g. Wedding (required)
  weight        – weight in grams, e.g. 8.5 (required)
  purity        – e.g. 22K (required)
  makingCharge  – making charge percentage, e.g. 12 (required)
  price         – price in rupees, e.g. 54000 (required)
  image         – image file (.jpg, .jpeg, .png, or .webp, max 5 MB) (optional)
```

### List all products
```
GET /products
```

### Get a single product
```
GET /products/{id}
```

### Update a product *(Owner only)*
```
PUT /products/{id}
Authorization: ******
Content-Type: application/json

{
  "name": "Gold Necklace Updated",
  "collection": "Wedding",
  "weight": 9.0,
  "purity": "22K",
  "makingCharge": 13,
  "price": 58000
}
```

### Delete a product *(Owner only)*
```
DELETE /products/{id}
Authorization: ******
```

---

## Database reset

If you need to start fresh (e.g. after a schema change):

1. Open **SQL Server Object Explorer** in Visual Studio (or use `sqlcmd`)
2. Delete the `JewelleryDb` database
3. Restart the app – tables will be recreated automatically

---

## Security notes

- Passwords are hashed with **PBKDF2-SHA256** (100,000 iterations + random salt). Never stored in plain text.
- Product creation requires a valid JWT with the `Owner` role.
- Uploaded images are validated for extension and size (max 5 MB).
- The JWT key **must** be changed from the placeholder before deploying to production.
