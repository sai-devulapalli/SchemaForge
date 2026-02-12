-- SchemaForge Test Seed Data for SQL Server
-- Creates test tables with various data types, PKs, FKs, identity columns, and sample data

USE schemaforge_test;
GO

-- Drop views first
IF OBJECT_ID('dbo.vw_EmployeeDepartments', 'V') IS NOT NULL DROP VIEW dbo.vw_EmployeeDepartments;
IF OBJECT_ID('dbo.vw_OrderSummary', 'V') IS NOT NULL DROP VIEW dbo.vw_OrderSummary;
GO

-- Drop tables in reverse dependency order if they exist
IF OBJECT_ID('dbo.OrderDetails', 'U') IS NOT NULL DROP TABLE dbo.OrderDetails;
IF OBJECT_ID('dbo.OrderHeaders', 'U') IS NOT NULL DROP TABLE dbo.OrderHeaders;
IF OBJECT_ID('dbo.Products', 'U') IS NOT NULL DROP TABLE dbo.Products;
IF OBJECT_ID('dbo.Employees', 'U') IS NOT NULL DROP TABLE dbo.Employees;
IF OBJECT_ID('dbo.Departments', 'U') IS NOT NULL DROP TABLE dbo.Departments;
GO

-- Table 1: Departments (no FK dependencies - root table)
CREATE TABLE dbo.Departments (
    DepartmentId INT IDENTITY(1,1) NOT NULL,
    DepartmentName NVARCHAR(100) NOT NULL,
    DepartmentCode VARCHAR(10) NOT NULL,
    Budget DECIMAL(15,2) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedDate DATETIME NOT NULL DEFAULT GETDATE(),
    Notes NVARCHAR(MAX) NULL,
    CONSTRAINT PK_Departments PRIMARY KEY (DepartmentId)
);
GO

-- Table 2: Employees (FK to Departments)
CREATE TABLE dbo.Employees (
    EmployeeId INT IDENTITY(1,1) NOT NULL,
    FirstName NVARCHAR(50) NOT NULL,
    LastName NVARCHAR(50) NOT NULL,
    Email VARCHAR(100) NOT NULL,
    DepartmentId INT NOT NULL,
    HireDate DATE NOT NULL,
    Salary DECIMAL(12,2) NULL,
    IsFullTime BIT NOT NULL DEFAULT 1,
    Rating FLOAT NULL,
    CONSTRAINT PK_Employees PRIMARY KEY (EmployeeId),
    CONSTRAINT FK_Employees_Departments FOREIGN KEY (DepartmentId) REFERENCES dbo.Departments(DepartmentId)
);
GO

-- Table 3: Products (no FK dependencies - root table)
CREATE TABLE dbo.Products (
    ProductId INT IDENTITY(1,1) NOT NULL,
    ProductName NVARCHAR(200) NOT NULL,
    SKU VARCHAR(50) NOT NULL,
    UnitPrice DECIMAL(10,2) NOT NULL,
    StockQuantity INT NOT NULL DEFAULT 0,
    Weight FLOAT NULL,
    IsDiscontinued BIT NOT NULL DEFAULT 0,
    Description NVARCHAR(MAX) NULL,
    CreatedDate DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_Products PRIMARY KEY (ProductId)
);
GO

-- Table 4: OrderHeaders (FK to Employees)
CREATE TABLE dbo.OrderHeaders (
    OrderId INT IDENTITY(1,1) NOT NULL,
    OrderNumber VARCHAR(20) NOT NULL,
    EmployeeId INT NOT NULL,
    OrderDate DATETIME NOT NULL DEFAULT GETDATE(),
    TotalAmount DECIMAL(15,2) NOT NULL DEFAULT 0,
    Status VARCHAR(20) NOT NULL DEFAULT 'Pending',
    ShippingAddress NVARCHAR(500) NULL,
    CONSTRAINT PK_OrderHeaders PRIMARY KEY (OrderId),
    CONSTRAINT FK_OrderHeaders_Employees FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(EmployeeId)
);
GO

-- Table 5: OrderDetails (composite FK to OrderHeaders + Products)
CREATE TABLE dbo.OrderDetails (
    OrderDetailId INT IDENTITY(1,1) NOT NULL,
    OrderId INT NOT NULL,
    ProductId INT NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(10,2) NOT NULL,
    Discount DECIMAL(5,2) NOT NULL DEFAULT 0,
    CONSTRAINT PK_OrderDetails PRIMARY KEY (OrderDetailId),
    CONSTRAINT FK_OrderDetails_Orders FOREIGN KEY (OrderId) REFERENCES dbo.OrderHeaders(OrderId),
    CONSTRAINT FK_OrderDetails_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products(ProductId)
);
GO

-- ============================================================
-- INDEXES (non-PK)
-- ============================================================
CREATE UNIQUE INDEX IX_Employees_Email ON dbo.Employees(Email);
CREATE INDEX IX_Products_SKU ON dbo.Products(SKU);
CREATE INDEX IX_OrderDetails_OrderProduct ON dbo.OrderDetails(OrderId, ProductId);
GO

-- ============================================================
-- CHECK & UNIQUE CONSTRAINTS
-- ============================================================
ALTER TABLE dbo.Employees ADD CONSTRAINT CK_Employees_Salary CHECK (Salary >= 0);
ALTER TABLE dbo.Departments ADD CONSTRAINT UQ_Departments_Code UNIQUE (DepartmentCode);
GO

-- ============================================================
-- VIEWS
-- ============================================================
CREATE VIEW dbo.vw_EmployeeDepartments AS
SELECT
    e.EmployeeId,
    e.FirstName,
    e.LastName,
    e.Email,
    d.DepartmentName,
    d.DepartmentCode,
    e.Salary,
    e.HireDate
FROM dbo.Employees e
INNER JOIN dbo.Departments d ON e.DepartmentId = d.DepartmentId;
GO

CREATE VIEW dbo.vw_OrderSummary AS
SELECT
    oh.OrderId,
    oh.OrderNumber,
    oh.OrderDate,
    oh.TotalAmount,
    oh.Status,
    e.FirstName + ' ' + e.LastName AS EmployeeName,
    COUNT(od.OrderDetailId) AS ItemCount
FROM dbo.OrderHeaders oh
INNER JOIN dbo.Employees e ON oh.EmployeeId = e.EmployeeId
LEFT JOIN dbo.OrderDetails od ON oh.OrderId = od.OrderId
GROUP BY oh.OrderId, oh.OrderNumber, oh.OrderDate, oh.TotalAmount, oh.Status,
         e.FirstName, e.LastName;
GO

-- ============================================================
-- SEED DATA
-- ============================================================

-- Departments (5 rows)
SET IDENTITY_INSERT dbo.Departments ON;
INSERT INTO dbo.Departments (DepartmentId, DepartmentName, DepartmentCode, Budget, IsActive, CreatedDate, Notes)
VALUES
    (1, 'Engineering', 'ENG', 500000.00, 1, '2024-01-15 08:00:00', 'Software engineering department'),
    (2, 'Sales', 'SAL', 300000.00, 1, '2024-01-15 08:00:00', 'Sales and business development'),
    (3, 'Human Resources', 'HR', 200000.00, 1, '2024-01-15 08:00:00', NULL),
    (4, 'Marketing', 'MKT', 250000.00, 1, '2024-02-01 09:00:00', 'Digital and traditional marketing'),
    (5, 'Finance', 'FIN', 180000.00, 0, '2024-03-01 10:00:00', 'Merged with accounting');
SET IDENTITY_INSERT dbo.Departments OFF;
GO

-- Employees (10 rows)
SET IDENTITY_INSERT dbo.Employees ON;
INSERT INTO dbo.Employees (EmployeeId, FirstName, LastName, Email, DepartmentId, HireDate, Salary, IsFullTime, Rating)
VALUES
    (1, 'Alice', 'Johnson', 'alice.johnson@example.com', 1, '2022-03-15', 95000.00, 1, 4.5),
    (2, 'Bob', 'Smith', 'bob.smith@example.com', 1, '2022-06-01', 88000.00, 1, 4.2),
    (3, 'Carol', 'Williams', 'carol.williams@example.com', 2, '2023-01-10', 72000.00, 1, 3.8),
    (4, 'David', 'Brown', 'david.brown@example.com', 2, '2023-04-20', 68000.00, 0, 3.5),
    (5, 'Eve', 'Davis', 'eve.davis@example.com', 3, '2021-11-01', 82000.00, 1, 4.8),
    (6, 'Frank', 'Miller', 'frank.miller@example.com', 1, '2023-08-15', 92000.00, 1, 4.1),
    (7, 'Grace', 'Wilson', 'grace.wilson@example.com', 4, '2022-09-01', 75000.00, 1, 4.0),
    (8, 'Henry', 'Taylor', 'henry.taylor@example.com', 4, '2023-02-14', 71000.00, 1, 3.9),
    (9, 'Ivy', 'Anderson', 'ivy.anderson@example.com', 5, '2022-07-20', 85000.00, 1, 4.3),
    (10, 'Jack', 'O''Brien', 'jack.obrien@example.com', 3, '2024-01-08', 0.00, 0, NULL);
SET IDENTITY_INSERT dbo.Employees OFF;
GO

-- Products (10 rows)
SET IDENTITY_INSERT dbo.Products ON;
INSERT INTO dbo.Products (ProductId, ProductName, SKU, UnitPrice, StockQuantity, Weight, IsDiscontinued, Description, CreatedDate)
VALUES
    (1, 'Wireless Mouse', 'WM-001', 29.99, 150, 0.15, 0, 'Ergonomic wireless mouse with USB receiver', '2024-01-01 00:00:00'),
    (2, 'Mechanical Keyboard', 'KB-002', 89.99, 75, 0.95, 0, 'Cherry MX Blue switches, full-size', '2024-01-01 00:00:00'),
    (3, 'USB-C Hub', 'HB-003', 49.99, 200, 0.12, 0, '7-port USB-C hub with HDMI', '2024-01-15 00:00:00'),
    (4, 'Monitor Stand', 'MS-004', 39.99, 50, 2.5, 0, 'Adjustable height monitor stand', '2024-02-01 00:00:00'),
    (5, 'Webcam HD', 'WC-005', 59.99, 100, 0.2, 0, '1080p HD webcam with microphone', '2024-02-01 00:00:00'),
    (6, 'Desk Lamp', 'DL-006', 34.99, 80, 1.2, 0, 'LED desk lamp with adjustable brightness', '2024-02-15 00:00:00'),
    (7, 'Mouse Pad XL', 'MP-007', 19.99, 300, 0.3, 0, 'Extended mouse pad 900x400mm', '2024-03-01 00:00:00'),
    (8, 'Cable Organizer', 'CO-008', 12.99, 500, 0.08, 0, NULL, '2024-03-01 00:00:00'),
    (9, 'Laptop Stand', 'LS-009', 44.99, 0, 1.5, 1, 'Discontinued aluminum laptop stand', '2024-01-01 00:00:00'),
    (10, 'Headset Pro', 'HS-010', 129.99, 60, 0.35, 0, 'Noise-cancelling USB headset', '2024-03-15 00:00:00');
SET IDENTITY_INSERT dbo.Products OFF;
GO

-- OrderHeaders (8 rows)
SET IDENTITY_INSERT dbo.OrderHeaders ON;
INSERT INTO dbo.OrderHeaders (OrderId, OrderNumber, EmployeeId, OrderDate, TotalAmount, Status, ShippingAddress)
VALUES
    (1, 'ORD-2024-001', 1, '2024-03-01 10:30:00', 169.97, 'Completed', '123 Main St, New York, NY 10001'),
    (2, 'ORD-2024-002', 3, '2024-03-05 14:15:00', 89.99, 'Completed', '456 Oak Ave, Los Angeles, CA 90001'),
    (3, 'ORD-2024-003', 2, '2024-03-10 09:00:00', 259.96, 'Shipped', '789 Pine Rd, Chicago, IL 60601'),
    (4, 'ORD-2024-004', 5, '2024-03-15 11:45:00', 49.99, 'Completed', NULL),
    (5, 'ORD-2024-005', 1, '2024-03-20 16:00:00', 174.97, 'Pending', '321 Elm Blvd, Houston, TX 77001'),
    (6, 'ORD-2024-006', 7, '2024-03-22 08:30:00', 129.99, 'Completed', '654 Maple Ct, Phoenix, AZ 85001'),
    (7, 'ORD-2024-007', 4, '2024-03-25 13:20:00', 59.98, 'Cancelled', '987 Cedar Ln, Seattle, WA 98101'),
    (8, 'ORD-2024-008', 6, '2024-03-28 10:00:00', 319.95, 'Shipped', '147 Birch Way, Denver, CO 80201');
SET IDENTITY_INSERT dbo.OrderHeaders OFF;
GO

-- OrderDetails (15 rows)
SET IDENTITY_INSERT dbo.OrderDetails ON;
INSERT INTO dbo.OrderDetails (OrderDetailId, OrderId, ProductId, Quantity, UnitPrice, Discount)
VALUES
    (1, 1, 1, 2, 29.99, 0.00),
    (2, 1, 3, 1, 49.99, 0.00),
    (3, 1, 7, 3, 19.99, 0.00),
    (4, 2, 2, 1, 89.99, 0.00),
    (5, 3, 2, 2, 89.99, 5.00),
    (6, 3, 5, 1, 59.99, 0.00),
    (7, 3, 7, 1, 19.99, 0.00),
    (8, 4, 3, 1, 49.99, 0.00),
    (9, 5, 10, 1, 129.99, 0.00),
    (10, 5, 4, 1, 39.99, 5.00),
    (11, 6, 10, 1, 129.99, 0.00),
    (12, 7, 1, 1, 29.99, 0.00),
    (13, 7, 7, 1, 19.99, 10.00),
    (14, 8, 2, 3, 89.99, 5.00),
    (15, 8, 6, 1, 34.99, 0.00);
SET IDENTITY_INSERT dbo.OrderDetails OFF;
GO

-- Verify seed data
SELECT 'Departments' AS TableName, COUNT(*) AS [RowCount] FROM dbo.Departments
UNION ALL
SELECT 'Employees', COUNT(*) FROM dbo.Employees
UNION ALL
SELECT 'Products', COUNT(*) FROM dbo.Products
UNION ALL
SELECT 'OrderHeaders', COUNT(*) FROM dbo.OrderHeaders
UNION ALL
SELECT 'OrderDetails', COUNT(*) FROM dbo.OrderDetails;
GO
