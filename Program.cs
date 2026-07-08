using System;
using System.Data;
using Microsoft.Data.SqlClient;

class Program
{
    static string connectionString = "Server=(localdb)\\MSSQLLocalDB;Database=JewelleryDb;Trusted_Connection=True;TrustServerCertificate=True;";

    static void Main()
    {
        EnsureDatabase();
        Console.WriteLine("Jewellery database is ready.");
        Console.WriteLine("Choose an action:");
        Console.WriteLine("1. Register Owner");
        Console.WriteLine("2. Register User");
        Console.WriteLine("3. Add Product");
        Console.WriteLine("4. View Products");
        Console.WriteLine("5. Exit");

        while (true)
        {
            Console.Write("Enter choice: ");
            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    RegisterOwner();
                    break;
                case "2":
                    RegisterUser();
                    break;
                case "3":
                    AddProduct();
                    break;
                case "4":
                    ViewProducts();
                    break;
                case "5":
                    return;
                default:
                    Console.WriteLine("Invalid choice.");
                    break;
            }
        }
    }

    static void EnsureDatabase()
    {
        using var masterConnection = new SqlConnection("Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;");
        masterConnection.Open();

        using var command = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = 'JewelleryDb')
            BEGIN
                CREATE DATABASE JewelleryDb;
            END", masterConnection);
        command.ExecuteNonQuery();

        using var dbConnection = new SqlConnection(connectionString);
        dbConnection.Open();

        using var createTables = new SqlCommand(@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Owners')
            BEGIN
                CREATE TABLE Owners (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Name NVARCHAR(100) NOT NULL,
                    Email NVARCHAR(100) NOT NULL UNIQUE,
                    PasswordHash NVARCHAR(255) NOT NULL,
                    Phone NVARCHAR(20) NULL,
                    CreatedAt DATETIME2 DEFAULT GETDATE()
                );
            END

            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users')
            BEGIN
                CREATE TABLE Users (
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    FullName NVARCHAR(100) NOT NULL,
                    Email NVARCHAR(100) NOT NULL UNIQUE,
                    PasswordHash NVARCHAR(255) NOT NULL,
                    Phone NVARCHAR(20) NULL,
                    CreatedAt DATETIME2 DEFAULT GETDATE()
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
                    CreatedAt DATETIME2 DEFAULT GETDATE()
                );
            END", dbConnection);
        createTables.ExecuteNonQuery();
    }

    static void RegisterOwner()
    {
        Console.Write("Owner name: ");
        var name = Console.ReadLine() ?? "";
        Console.Write("Owner email: ");
        var email = Console.ReadLine() ?? "";
        Console.Write("Owner password: ");
        var password = Console.ReadLine() ?? "";
        Console.Write("Owner phone: ");
        var phone = Console.ReadLine() ?? "";

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        using var command = new SqlCommand(@"
            INSERT INTO Owners (Name, Email, PasswordHash, Phone)
            VALUES (@Name, @Email, @PasswordHash, @Phone)", connection);

        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Email", email);
        command.Parameters.AddWithValue("@PasswordHash", HashPassword(password));
        command.Parameters.AddWithValue("@Phone", phone);
        command.ExecuteNonQuery();
        Console.WriteLine("Owner registered successfully.");
    }

    static void RegisterUser()
    {
        Console.Write("User full name: ");
        var name = Console.ReadLine() ?? "";
        Console.Write("User email: ");
        var email = Console.ReadLine() ?? "";
        Console.Write("User password: ");
        var password = Console.ReadLine() ?? "";
        Console.Write("User phone: ");
        var phone = Console.ReadLine() ?? "";

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        using var command = new SqlCommand(@"
            INSERT INTO Users (FullName, Email, PasswordHash, Phone)
            VALUES (@FullName, @Email, @PasswordHash, @Phone)", connection);

        command.Parameters.AddWithValue("@FullName", name);
        command.Parameters.AddWithValue("@Email", email);
        command.Parameters.AddWithValue("@PasswordHash", HashPassword(password));
        command.Parameters.AddWithValue("@Phone", phone);
        command.ExecuteNonQuery();
        Console.WriteLine("User registered successfully.");
    }

    static void AddProduct()
    {
        Console.Write("Product name: ");
        var name = Console.ReadLine() ?? "";
        Console.Write("Collection: ");
        var collection = Console.ReadLine() ?? "";
        Console.Write("Weight (grams): ");
        var weight = decimal.Parse(Console.ReadLine() ?? "0");
        Console.Write("Purity: ");
        var purity = Console.ReadLine() ?? "22K";
        Console.Write("Making charge (%): ");
        var makingCharge = decimal.Parse(Console.ReadLine() ?? "0");
        Console.Write("Price: ");
        var price = decimal.Parse(Console.ReadLine() ?? "0");
        Console.Write("Photo path (optional): ");
        var photoPath = Console.ReadLine() ?? "";

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        using var command = new SqlCommand(@"
            INSERT INTO Products (Name, Collection, Weight, Purity, MakingCharge, Price, PhotoPath)
            VALUES (@Name, @Collection, @Weight, @Purity, @MakingCharge, @Price, @PhotoPath)", connection);

        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Collection", collection);
        command.Parameters.AddWithValue("@Weight", weight);
        command.Parameters.AddWithValue("@Purity", purity);
        command.Parameters.AddWithValue("@MakingCharge", makingCharge);
        command.Parameters.AddWithValue("@Price", price);
        command.Parameters.AddWithValue("@PhotoPath", string.IsNullOrWhiteSpace(photoPath) ? (object)DBNull.Value : photoPath);
        command.ExecuteNonQuery();
        Console.WriteLine("Product added successfully.");
    }

    static void ViewProducts()
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand("SELECT Id, Name, Collection, Weight, Purity, MakingCharge, Price, PhotoPath FROM Products ORDER BY Id", connection);
        using var reader = command.ExecuteReader();

        Console.WriteLine("\nProducts:");
        while (reader.Read())
        {
            Console.WriteLine($"{reader["Id"]}. {reader["Name"]} | {reader["Collection"]} | {reader["Weight"]}g | {reader["Purity"]} | ₹{reader["Price"]}");
        }
    }

    static string HashPassword(string password)
    {
        return Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password)));
    }
}
