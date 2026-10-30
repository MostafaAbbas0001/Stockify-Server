namespace Stockify.Data;

/// <summary>Starter data copied into every new tenant by TenantProvisioningService.</summary>
public static class DefaultTenantData
{
    public sealed record ExpenseCategoryTemplate(string Code, string Name, string Description, string? ParentCode);

    /// <summary>Parent-child expense category tree. Parents are listed before their children.</summary>
    public static readonly IReadOnlyList<ExpenseCategoryTemplate> ExpenseCategories = new ExpenseCategoryTemplate[]
    {
        new("1000", "Employee Expenses", "Parent category for all employee-related expenses, including compensation, benefits, and allowances.", null),
        new("2000", "Office Supplies & Equipment", "Parent category for all physical assets, consumables, and equipment required for daily office operations.", null),
        new("3000", "Maintenance & Repairs", "Parent category for all maintenance services and repair work on facilities, equipment, and infrastructure.", null),
        new("4000", "Operating Expenses", "Parent category for essential day-to-day business expenses, including utilities, rent, and other operational costs.", null),
        new("5000", "Company Insurance Expenses", "Parent category for all insurance premiums and coverage costs for the company.", null),
        new("6000", "Marketing & Advertising", "Parent category for promotional activities, advertising campaigns, and marketing initiatives.", null),

        // Children of Employee Expenses (ParentId = 1000)
        new("1100", "Salaries", "Fixed monthly compensation for full-time and part-time employees.", "1000"),
        new("1200", "Overtime", "Additional compensation for hours worked beyond standard working hours.", "1000"),
        new("1300", "Bonuses", "Performance-based incentives and discretionary bonuses.", "1000"),
        new("1400", "Commissions", "Variable compensation based on sales performance or other metrics.", "1000"),
        new("1500", "Allowances", "Regular allowances for transportation, meals, housing, etc.", "1000"),
        new("1600", "Social Security", "Mandatory employer contributions to social security and pension funds.", "1000"),
        new("1700", "Employee Insurance", "Health, life, and other insurance plans for employees.", "1000"),
        new("1800", "Employee Social Security Deductions", "Social security deductions made on behalf of employees.", "1000"),
        new("1900", "Other", "Other employee expenses not falling into specific categories.", "1000"),

        // Children of Office Supplies & Equipment (ParentId = 2000)
        new("2100", "Office Supplies", "Daily consumables like pens, paper, and printer ink.", "2000"),
        new("2200", "Furniture", "Desks, chairs, filing cabinets, and other office furnishings.", "2000"),
        new("2300", "IT & Electronic Equipment", "Computers, laptops, monitors, and peripherals.", "2000"),
        new("2400", "Security Equipment", "Security equipment, cameras, and alarms.", "2000"),
        new("2500", "Pantry And Kitchen Supplies", "Kitchen appliances, coffee, and break room supplies.", "2000"),
        new("2600", "Sanitary Supplies", "Restroom supplies, cleaning materials, and sanitary fixtures.", "2000"),
        new("2700", "Hospitality Supplies", "Supplies for guest reception and client meetings.", "2000"),
        new("2800", "HVAC Equipment", "Heating, ventilation, and air conditioning systems.", "2000"),
        new("2900", "Energy & Solar Equipment", "Solar panels, inverters, and renewable energy equipment.", "2000"),
        new("2910", "Electrical Supplies", "Electrical wiring, outlets, switches, and installation materials.", "2000"),
        new("2920", "Cleaning Supplies", "Cleaning chemicals, janitorial equipment, and supplies.", "2000"),
        new("2930", "Other Office Supplies", "Other office supplies not falling into specific categories.", "2000"),

        // Children of Maintenance & Repairs (ParentId = 3000)
        new("3100", "Sanitary Maintenance", "Regular maintenance of building structure, plumbing, electrical systems, and facility management services", "3000"),
        new("3200", "Electrical Maintenance", "Regular maintenance of building structure, plumbing, electrical systems, and facility management services", "3000"),
        new("3300", "IT Maintenance & Support", "Technical support, software updates, network maintenance, and IT infrastructure management", "3000"),
        new("3400", "Painting & General Repairs", "Regular maintenance of building structure, plumbing, electrical systems, and facility management services", "3000"),
        new("3500", "HVAC Maintenance", "Regular maintenance of building structure, plumbing, electrical systems, and facility management services", "3000"),
        new("3600", "Renewable Energy Maintenance", "Regular maintenance of building structure, plumbing, electrical systems, and facility management services", "3000"),
        new("3700", "Other Maintenance & Repairs", "Regular maintenance of building structure, plumbing, electrical systems, and facility management services", "3000"),

        // Children of Operating Expenses (ParentId = 4000)
        new("4101", "Rent", "Monthly rental payments for office space, warehouses, retail locations, and other business premises", "4000"),
        new("4102", "Electricity Bills", "Electricity consumption for business operations", "4000"),
        new("4103", "Water Supply Charges", "Water consumption for business operations", "4000"),
        new("4104", "Internet & Networking Services", "Internet connectivity, networking equipment, and telecommunications services", "4000"),
        new("4105", "Landline Phone Subscription", "Landline phone subscription and related telecommunications services", "4000"),
        new("4106", "Diesel / Generator Fuel Expense", "Diesel or generator fuel expenses for backup power generation", "4000"),
        new("4107", "Building Service Charges", "Building management fees, common area maintenance, and shared facility service charges", "4000"),
        new("4108", "HVAC Energy Costs", "Energy costs for heating, ventilation, and air conditioning systems", "4000"),
        new("4109", "Cleaning Services", "Professional cleaning services for office spaces, warehouses, and other business premises", "4000"),
        new("4110", "Travel Expenses", "Business travel costs including flights, hotels, meals, transportation, and related travel expenses", "4000"),
        new("4111", "Third-Party Commissions", "Commissions paid to external agents, brokers, consultants, and third-party service providers", "4000"),
        new("4112", "Licenses & Software Subscriptions", "Annual software subscriptions, cloud services, and professional license renewals", "4000"),
        new("4113", "Other Operating Expenses", "Miscellaneous operational costs not covered by other categories including bank fees, legal fees, and administrative expenses", "4000"),

        // Children of Company Insurance Expenses (ParentId = 5000)
        new("5100", "Property Insurance", "Property insurance for buildings, warehouses, and business premises", "5000"),
        new("5101", "Vehicle Insurance", "Vehicle insurance for company vehicles, delivery trucks, and transportation equipment", "5000"),
        new("5102", "Workmen Compensation Insurance", "Workmen compensation insurance for employees", "5000"),
        new("5103", "Public Liability Insurance", "Public liability insurance for business operations", "5000"),
        new("5104", "Other Company Insurance", "Other company insurance expenses", "5000"),

        // Children of Marketing & Advertising (ParentId = 6000)
        new("6100", "Social Media Content & Management", "Online advertising campaigns, social media marketing, SEO services, and digital promotional activities", "6000"),
        new("6200", "Paid Advertising / Boosts", "Newspaper ads, magazine placements, flyers, brochures, and other print media advertising", "6000"),
        new("6300", "Print Materials", "Trade shows, conferences, promotional events, sponsorships, and marketing event participation", "6000"),
        new("6400", "Promotional Items", "Logo design, brand identity development, marketing materials, and brand consulting services", "6000"),
        new("6500", "Sponsorships & Events", "Logo design, brand identity development, marketing materials, and brand consulting services", "6000"),
        new("6600", "Other Marketing Expenses", "Other marketing expenses", "6000"),
    };
}
