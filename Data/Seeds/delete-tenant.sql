-- Deletes one company and every row it owns. Development helper for re-running the demo seed:
--   psql -v slug=urban-threads -f Data/Seeds/delete-tenant.sql
-- Children are deleted before parents; the self-referencing ExpenseCategories tree and the two
-- many-to-many join tables (no TenantId column) are handled explicitly.
\set ON_ERROR_STOP on
BEGIN;

SELECT "Id" AS tenant_id FROM "Tenants" WHERE "Slug" = :'slug' \gset
\if :{?tenant_id}
\else
  \echo 'No tenant with slug' :slug
  ROLLBACK;
  \quit
\endif

\echo 'Deleting tenant' :tenant_id '(' :slug ')'

DELETE FROM "UserPermissions"        WHERE "TenantId" = :tenant_id;
DELETE FROM "RefreshTokens"          WHERE "TenantId" = :tenant_id;
DELETE FROM "ExpenseLinePayments"    WHERE "TenantId" = :tenant_id;
DELETE FROM "ExpenseLines"           WHERE "TenantId" = :tenant_id;
DELETE FROM "Expenses"               WHERE "TenantId" = :tenant_id;
DELETE FROM "ExpenseCategories"      WHERE "TenantId" = :tenant_id;
DELETE FROM "CheckoutKeys"           WHERE "TenantId" = :tenant_id;
DELETE FROM "CreditMemoItems"        WHERE "TenantId" = :tenant_id;
DELETE FROM "CreditMemos"            WHERE "TenantId" = :tenant_id;
DELETE FROM "Payments"               WHERE "TenantId" = :tenant_id;
DELETE FROM "InvoiceItems"           WHERE "TenantId" = :tenant_id;
DELETE FROM "Invoices"               WHERE "TenantId" = :tenant_id;
DELETE FROM "OrderItemLots"          WHERE "TenantId" = :tenant_id;
DELETE FROM "OrderItems"             WHERE "TenantId" = :tenant_id;
DELETE FROM "Orders"                 WHERE "TenantId" = :tenant_id;
DELETE FROM "StockTransferItemLots"  WHERE "TenantId" = :tenant_id;
DELETE FROM "StockTransferItems"     WHERE "TenantId" = :tenant_id;
DELETE FROM "StockTransfers"         WHERE "TenantId" = :tenant_id;
DELETE FROM "InventoryAdjustments"   WHERE "TenantId" = :tenant_id;
DELETE FROM "StockMovements"         WHERE "TenantId" = :tenant_id;
DELETE FROM "BranchLotStocks"        WHERE "TenantId" = :tenant_id;
DELETE FROM "LocationStocks"         WHERE "TenantId" = :tenant_id;
DELETE FROM "InventoryLots"          WHERE "TenantId" = :tenant_id;
DELETE FROM "ProductVariantAttributeValues"
  WHERE "ProductVariantId" IN (SELECT "Id" FROM "ProductVariants" WHERE "TenantId" = :tenant_id);
DELETE FROM "ProductAttributeAssignments"
  WHERE "ProductId" IN (SELECT "Id" FROM "Products" WHERE "TenantId" = :tenant_id);
DELETE FROM "ProductVariants"        WHERE "TenantId" = :tenant_id;
DELETE FROM "ProductAttributeValues" WHERE "TenantId" = :tenant_id;
DELETE FROM "ProductAttributes"      WHERE "TenantId" = :tenant_id;
DELETE FROM "Products"               WHERE "TenantId" = :tenant_id;
DELETE FROM "Brands"                 WHERE "TenantId" = :tenant_id;
DELETE FROM "Categories"             WHERE "TenantId" = :tenant_id;
DELETE FROM "Sales"                  WHERE "TenantId" = :tenant_id;
DELETE FROM "Employees"              WHERE "TenantId" = :tenant_id;
DELETE FROM "Customers"              WHERE "TenantId" = :tenant_id;
DELETE FROM "DeliveryLocations"      WHERE "TenantId" = :tenant_id;
DELETE FROM "Currencies"             WHERE "TenantId" = :tenant_id;
DELETE FROM "TaxRates"               WHERE "TenantId" = :tenant_id;
UPDATE "Tenants" SET "OwnerUserId" = NULL WHERE "Id" = :tenant_id;
DELETE FROM "Users"                  WHERE "TenantId" = :tenant_id;
DELETE FROM "Branches"               WHERE "TenantId" = :tenant_id;
DELETE FROM "TenantSequences"        WHERE "TenantId" = :tenant_id;
DELETE FROM "Tenants"                WHERE "Id" = :tenant_id;

COMMIT;
\echo 'Done.'
