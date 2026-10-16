using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using ePrescriptions.Core.Helpers;
using ePrescriptions.Domain.Entities;
using ePrescriptions.Domain.Enums;

namespace ePrescriptions.Data.Repositories
{
    public class SupplierRepository
    {
        private readonly DatabaseContext _dbContext;

        public SupplierRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public List<Supplier> GetAll()
        {
            var list = new List<Supplier>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Phone, TaxNumber, Address, BalancePiastres, IsActive FROM Suppliers WHERE IsActive = 1 ORDER BY Name ASC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Supplier
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = reader["Name"].ToString() ?? string.Empty,
                    Phone = reader["Phone"].ToString() ?? string.Empty,
                    TaxNumber = reader["TaxNumber"] is DBNull ? null : reader["TaxNumber"].ToString(),
                    Address = reader["Address"] is DBNull ? null : reader["Address"].ToString(),
                    BalancePiastres = Convert.ToInt64(reader["BalancePiastres"]),
                    IsActive = Convert.ToInt32(reader["IsActive"]) == 1
                });
            }
            return list;
        }

        public Supplier? GetById(int id)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Phone, TaxNumber, Address, BalancePiastres, IsActive FROM Suppliers WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new Supplier
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = reader["Name"].ToString() ?? string.Empty,
                    Phone = reader["Phone"].ToString() ?? string.Empty,
                    TaxNumber = reader["TaxNumber"] is DBNull ? null : reader["TaxNumber"].ToString(),
                    Address = reader["Address"] is DBNull ? null : reader["Address"].ToString(),
                    BalancePiastres = Convert.ToInt64(reader["BalancePiastres"]),
                    IsActive = Convert.ToInt32(reader["IsActive"]) == 1
                };
            }
            return null;
        }

        public int SaveSupplier(Supplier supplier)
        {
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();

            if (supplier.Id == 0)
            {
                cmd.CommandText = @"
                    INSERT INTO Suppliers (Name, Phone, TaxNumber, Address, BalancePiastres, IsActive)
                    VALUES (@name, @phone, @tax, @addr, @bal, 1);
                    SELECT last_insert_rowid();";
            }
            else
            {
                cmd.CommandText = @"
                    UPDATE Suppliers SET
                        Name = @name,
                        Phone = @phone,
                        TaxNumber = @tax,
                        Address = @addr
                    WHERE Id = @id;";
                cmd.Parameters.AddWithValue("@id", supplier.Id);
            }

            cmd.Parameters.AddWithValue("@name", supplier.Name.Trim());
            cmd.Parameters.AddWithValue("@phone", supplier.Phone.Trim());
            cmd.Parameters.AddWithValue("@tax", (object?)supplier.TaxNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@addr", (object?)supplier.Address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bal", supplier.BalancePiastres);

            if (supplier.Id == 0)
            {
                supplier.Id = Convert.ToInt32(cmd.ExecuteScalar());
            }
            else
            {
                cmd.ExecuteNonQuery();
            }

            return supplier.Id;
        }
    }

    public class PurchaseRepository
    {
        private readonly DatabaseContext _dbContext;

        public PurchaseRepository(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        private static string PurchaseSequenceKey() => $"PUR-{DateTime.Now.Year}";

        /// <summary>الرقم المتوقع (للعرض)؛ الرقم النهائي بيتحجز داخل Transaction الحفظ</summary>
        public string GenerateNextPurchaseInvoiceNumber()
        {
            string key = PurchaseSequenceKey();
            using var conn = _dbContext.CreateConnection();
            return NumberSequence.Format(key, NumberSequence.Peek(conn, key, "PurchaseInvoices", "InvoiceNumber"));
        }

        public string GenerateNextSupplierVoucherNumber()
        {
            string year = DateTime.Now.Year.ToString();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM SupplierVouchers;";
            long count = Convert.ToInt64(cmd.ExecuteScalar() ?? 0) + 1;
            return $"SV-{year}-{count:D5}";
        }

        public bool SavePurchaseInvoice(PurchaseInvoice invoice, out string errorMessage)
        {
            errorMessage = string.Empty;
            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();

            try
            {
                string purKey = PurchaseSequenceKey();
                invoice.InvoiceNumber = NumberSequence.Format(purKey, NumberSequence.Next(conn, trans, purKey, "PurchaseInvoices", "InvoiceNumber"));

                // 1) حفظ رأس فاتورة الشراء
                using var cmdInv = conn.CreateCommand();
                cmdInv.Transaction = trans;
                cmdInv.CommandText = @"
                    INSERT INTO PurchaseInvoices (
                        InvoiceNumber, SupplierId, SupplierName, InvoiceDate,
                        SubtotalPiastres, VatPiastres, TotalPiastres, PaidPiastres,
                        RemainingPiastres, Notes, CreatedAt
                    ) VALUES (
                        @num, @sId, @sName, @date,
                        @sub, @vat, @tot, @paid,
                        @rem, @notes, datetime('now')
                    );
                    SELECT last_insert_rowid();";

                cmdInv.Parameters.AddWithValue("@num", invoice.InvoiceNumber);
                cmdInv.Parameters.AddWithValue("@sId", invoice.SupplierId);
                cmdInv.Parameters.AddWithValue("@sName", invoice.SupplierName);
                cmdInv.Parameters.AddWithValue("@date", invoice.InvoiceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                cmdInv.Parameters.AddWithValue("@sub", invoice.SubtotalPiastres);
                cmdInv.Parameters.AddWithValue("@vat", invoice.VatPiastres);
                cmdInv.Parameters.AddWithValue("@tot", invoice.TotalPiastres);
                cmdInv.Parameters.AddWithValue("@paid", invoice.PaidPiastres);
                cmdInv.Parameters.AddWithValue("@rem", invoice.RemainingPiastres);
                cmdInv.Parameters.AddWithValue("@notes", (object?)invoice.Notes ?? DBNull.Value);

                int invId = Convert.ToInt32(cmdInv.ExecuteScalar());
                invoice.Id = invId;

                // 2) حفظ بنود الفاتورة وإضافة دفعات المخزون الجديدة
                foreach (var item in invoice.Items)
                {
                    item.PurchaseInvoiceId = invId;

                    using var cmdItem = conn.CreateCommand();
                    cmdItem.Transaction = trans;
                    cmdItem.CommandText = @"
                        INSERT INTO PurchaseInvoiceItems (
                            PurchaseInvoiceId, ItemId, ItemCode, ItemNameAr,
                            BatchNumber, ExpiryDate, UnitLevel, QuantityPurchased,
                            SmallestUnitsCount, UnitCostPiastres, VatPiastres, TotalPiastres, VatPercent
                        ) VALUES (
                            @pId, @itemId, @code, @name,
                            @batch, @exp, @uLevel, @qty,
                            @smUnits, @cost, @vat, @tot, @vatPct
                        );
                        SELECT last_insert_rowid();";

                    cmdItem.Parameters.AddWithValue("@pId", invId);
                    cmdItem.Parameters.AddWithValue("@itemId", item.ItemId);
                    cmdItem.Parameters.AddWithValue("@code", item.ItemCode);
                    cmdItem.Parameters.AddWithValue("@name", item.ItemNameAr);
                    cmdItem.Parameters.AddWithValue("@batch", item.BatchNumber);
                    cmdItem.Parameters.AddWithValue("@exp", ExpiryPolicy.ToIso(item.ExpiryDate));
                    cmdItem.Parameters.AddWithValue("@uLevel", (int)item.UnitLevel);
                    cmdItem.Parameters.AddWithValue("@qty", item.QuantityPurchased);
                    cmdItem.Parameters.AddWithValue("@smUnits", item.SmallestUnitsCount);
                    cmdItem.Parameters.AddWithValue("@cost", item.UnitCostPiastres);
                    cmdItem.Parameters.AddWithValue("@vat", item.VatPiastres);
                    cmdItem.Parameters.AddWithValue("@tot", item.TotalPiastres);
                    cmdItem.Parameters.AddWithValue("@vatPct", item.VatPercent.HasValue ? (object)item.VatPercent.Value : DBNull.Value);
                    item.Id = Convert.ToInt32(cmdItem.ExecuteScalar());

                    // تسوية ما بيع بالسالب قبل دخول هذه الفاتورة: الوارد يسدد السالب أولاً، والباقي يدخل رصيد التشغيلة
                    int unitsPlacedInBatch = ItemRepository.AbsorbOversoldStock(conn, trans, item.ItemId, item.SmallestUnitsCount);

                    // إضافة الدفعة إلى ItemBatches
                    using var cmdBatch = conn.CreateCommand();
                    cmdBatch.Transaction = trans;
                    cmdBatch.CommandText = @"
                        INSERT INTO ItemBatches (
                            ItemId, BatchNumber, ExpiryDate, RemainingSmallestUnits,
                            CostPricePerSmallestUnitPiastres, CreatedAt
                        ) VALUES (
                            @itemId, @batch, @exp, @rem,
                            @costPerUnit, datetime('now')
                        );
                        SELECT last_insert_rowid();";

                    // تكلفة القطعة = (سعر الوحدة المشتراة × الكمية) ÷ إجمالي الوحدات الصغرى، بتقريب Half-Up
                    // (كان بيتقسم سعر الوحدة الواحدة على الإجمالي فتطلع التكلفة أقل من الحقيقي بمقدار الكمية)
                    long costPerSmallestUnit = item.SmallestUnitsCount > 0
                        ? (item.UnitCostPiastres * item.QuantityPurchased + item.SmallestUnitsCount / 2) / item.SmallestUnitsCount
                        : item.UnitCostPiastres;

                    cmdBatch.Parameters.AddWithValue("@itemId", item.ItemId);
                    cmdBatch.Parameters.AddWithValue("@batch", item.BatchNumber);
                    cmdBatch.Parameters.AddWithValue("@exp", ExpiryPolicy.ToIso(item.ExpiryDate));
                    cmdBatch.Parameters.AddWithValue("@rem", unitsPlacedInBatch);
                    cmdBatch.Parameters.AddWithValue("@costPerUnit", costPerSmallestUnit);
                    item.BatchId = Convert.ToInt32(cmdBatch.ExecuteScalar());

                    // ربط بند الشراء بتشغيلته (يستخدمه مرتجع المورد المرتبط بالفاتورة - خطوة 39)
                    using var cmdLink = conn.CreateCommand();
                    cmdLink.Transaction = trans;
                    cmdLink.CommandText = "UPDATE PurchaseInvoiceItems SET BatchId = @b WHERE Id = @id;";
                    cmdLink.Parameters.AddWithValue("@b", item.BatchId);
                    cmdLink.Parameters.AddWithValue("@id", item.Id);
                    cmdLink.ExecuteNonQuery();

                    // زيادة رصيد مخزون الصنف الكلي
                    using var cmdStock = conn.CreateCommand();
                    cmdStock.Transaction = trans;
                    cmdStock.CommandText = @"
                        UPDATE Items
                        SET TotalStockSmallestUnits = TotalStockSmallestUnits + @added
                        WHERE Id = @itemId;";
                    cmdStock.Parameters.AddWithValue("@added", item.SmallestUnitsCount);
                    cmdStock.Parameters.AddWithValue("@itemId", item.ItemId);
                    cmdStock.ExecuteNonQuery();
                }

                // 3) تحديث رصيد المورد إذا كان هناك متبقي
                if (invoice.RemainingPiastres > 0)
                {
                    using var cmdSupp = conn.CreateCommand();
                    cmdSupp.Transaction = trans;
                    cmdSupp.CommandText = @"
                        UPDATE Suppliers
                        SET BalancePiastres = BalancePiastres + @rem
                        WHERE Id = @sId;";
                    cmdSupp.Parameters.AddWithValue("@rem", invoice.RemainingPiastres);
                    cmdSupp.Parameters.AddWithValue("@sId", invoice.SupplierId);
                    cmdSupp.ExecuteNonQuery();
                }

                JournalPosting.PostPurchaseInvoice(conn, trans, invoice);

                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                errorMessage = ex.Message;
                Logger.LogException(ex, "SavePurchaseInvoice");
                return false;
            }
        }

        /// <summary>
        /// أوامر شراء مقترحة من قائمة النواقص (أصناف نشطة رصيدها <= حدها الأدنى)، مجمّعة بمورد آخر شراء لكل صنف.
        /// للقراءة فقط: مفيش حفظ ولا حركة مخزون ولا رقم مستند بيتحجز. الأصناف الموجودة في أمر شراء مفتوح بتتستبعد.
        /// </summary>
        public List<SuggestedPurchaseOrder> GetSuggestedPurchaseOrders()
        {
            var groups = new Dictionary<int, SuggestedPurchaseOrder>();
            var noSupplier = new SuggestedPurchaseOrder { SupplierId = null, SupplierName = "بدون مورد سابق" };

            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT i.Id, i.Code, i.NameAr, i.TotalStockSmallestUnits, i.MinStockAlertSmallestUnits,
                       i.StripsPerBox, i.PiecesPerStrip,
                       s.Id AS SupplierId, s.Name AS SupplierName,
                       p.UnitCostPiastres AS LastUnitCost, p.QuantityPurchased AS LastQty, p.SmallestUnitsCount AS LastUnits
                FROM Items i
                LEFT JOIN PurchaseInvoiceItems p ON p.Id = (
                    SELECT p2.Id FROM PurchaseInvoiceItems p2
                    JOIN PurchaseInvoices h2 ON h2.Id = p2.PurchaseInvoiceId
                    WHERE p2.ItemId = i.Id
                    ORDER BY h2.InvoiceDate DESC, p2.Id DESC LIMIT 1)
                LEFT JOIN PurchaseInvoices h ON h.Id = p.PurchaseInvoiceId
                LEFT JOIN Suppliers s ON s.Id = h.SupplierId AND s.IsActive = 1
                WHERE i.IsActive = 1 AND i.TotalStockSmallestUnits <= i.MinStockAlertSmallestUnits
                  AND i.Id NOT IN (SELECT poi.ItemId FROM PurchaseOrderItems poi
                                   JOIN PurchaseOrders po ON po.Id = poi.OrderId WHERE po.Status = 0)
                ORDER BY i.NameAr;";

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var line = new SuggestedOrderLine
                {
                    ItemId = Convert.ToInt32(r["Id"]),
                    ItemCode = r["Code"].ToString() ?? string.Empty,
                    ItemNameAr = r["NameAr"].ToString() ?? string.Empty,
                    CurrentStockSmallestUnits = Convert.ToInt32(r["TotalStockSmallestUnits"]),
                    MinStockAlertSmallestUnits = Convert.ToInt32(r["MinStockAlertSmallestUnits"]),
                    SmallestUnitsPerBox = Math.Max(1, Convert.ToInt32(r["StripsPerBox"]) * Convert.ToInt32(r["PiecesPerStrip"]))
                };
                if (line.SuggestedUnits <= 0) continue; // حد أدنى صفر ورصيد صفر: مفيش حاجة تتطلب

                if (!(r["LastUnits"] is DBNull))
                {
                    line.LastPurchaseUnits = Convert.ToInt32(r["LastUnits"]);
                    line.LastPurchaseTaxablePiastres = Convert.ToInt64(r["LastUnitCost"]) * Convert.ToInt32(r["LastQty"]);
                }

                if (r["SupplierId"] is DBNull)
                {
                    noSupplier.Lines.Add(line);
                    continue;
                }

                int supplierId = Convert.ToInt32(r["SupplierId"]);
                if (!groups.TryGetValue(supplierId, out var order))
                {
                    order = new SuggestedPurchaseOrder { SupplierId = supplierId, SupplierName = r["SupplierName"].ToString() ?? string.Empty };
                    groups[supplierId] = order;
                }
                order.Lines.Add(line);
            }

            var result = groups.Values.OrderBy(o => o.SupplierName, StringComparer.Ordinal).ToList();
            if (noSupplier.Lines.Count > 0) result.Add(noSupplier);
            return result;
        }

        public bool AddSupplierVoucher(SupplierVoucher voucher)
        {
            using var conn = _dbContext.CreateConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = trans;
                cmd.CommandText = @"
                    INSERT INTO SupplierVouchers (
                        VoucherNumber, SupplierId, SupplierName, Date,
                        AmountPiastres, PaymentMethod, Notes, CreatedAt
                    ) VALUES (
                        @num, @sId, @name, @date,
                        @amt, @pMethod, @notes, datetime('now')
                    );";

                cmd.Parameters.AddWithValue("@num", voucher.VoucherNumber);
                cmd.Parameters.AddWithValue("@sId", voucher.SupplierId);
                cmd.Parameters.AddWithValue("@name", voucher.SupplierName);
                cmd.Parameters.AddWithValue("@date", voucher.Date.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@amt", voucher.AmountPiastres);
                cmd.Parameters.AddWithValue("@pMethod", (int)voucher.PaymentMethod);
                cmd.Parameters.AddWithValue("@notes", (object?)voucher.Notes ?? DBNull.Value);
                cmd.ExecuteNonQuery();

                // خصم المبلغ المسدد من رصيد المورد
                using var cmdSupp = conn.CreateCommand();
                cmdSupp.Transaction = trans;
                cmdSupp.CommandText = @"
                    UPDATE Suppliers
                    SET BalancePiastres = BalancePiastres - @amt
                    WHERE Id = @sId;";
                cmdSupp.Parameters.AddWithValue("@amt", voucher.AmountPiastres);
                cmdSupp.Parameters.AddWithValue("@sId", voucher.SupplierId);
                cmdSupp.ExecuteNonQuery();

                trans.Commit();
                return true;
            }
            catch (Exception ex)
            {
                trans.Rollback();
                Logger.LogException(ex, "AddSupplierVoucher");
                return false;
            }
        }

        public List<PurchaseInvoice> GetAllPurchases(int limit = 50)
        {
            var list = new List<PurchaseInvoice>();
            using var conn = _dbContext.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, InvoiceNumber, SupplierId, SupplierName, InvoiceDate,
                       SubtotalPiastres, VatPiastres, TotalPiastres, PaidPiastres,
                       RemainingPiastres, Notes, CreatedAt
                FROM PurchaseInvoices
                ORDER BY Id DESC
                LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new PurchaseInvoice
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    InvoiceNumber = reader["InvoiceNumber"].ToString() ?? string.Empty,
                    SupplierId = Convert.ToInt32(reader["SupplierId"]),
                    SupplierName = reader["SupplierName"].ToString() ?? string.Empty,
                    InvoiceDate = DbDate.Parse(reader["InvoiceDate"].ToString() ?? DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                    SubtotalPiastres = Convert.ToInt64(reader["SubtotalPiastres"]),
                    VatPiastres = Convert.ToInt64(reader["VatPiastres"]),
                    TotalPiastres = Convert.ToInt64(reader["TotalPiastres"]),
                    PaidPiastres = Convert.ToInt64(reader["PaidPiastres"]),
                    RemainingPiastres = Convert.ToInt64(reader["RemainingPiastres"]),
                    Notes = reader["Notes"] is DBNull ? null : reader["Notes"].ToString(),
                    CreatedAt = DbDate.Parse(reader["CreatedAt"].ToString() ?? DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture))
                });
            }
            return list;
        }
    }
}
