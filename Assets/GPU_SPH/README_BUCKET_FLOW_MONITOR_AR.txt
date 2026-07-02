BucketFlowMonitor3D - Inside Only Helical Monitor

هذا السكربت Visualizer فقط ولا يغيّر محاكاة GPU SPH الأساسية.
يعرض حركة حلزونية داخل السطل، مستوى الماء، التدفق، ونقصان الماء.

الإصدار الحالي يمنع خروج المؤشرات البصرية من السطل:
- Tracer dots تبقى داخل حدود السطل.
- Vortex path يبقى داخل السطل.
- Flow arrow يبقى داخل السطل.
- Outlet preview paths تبقى داخل السطل.

أهم الإعدادات:
Keep Flow Visuals Inside Bucket = ON
Inside Wall Padding Multiplier = 0.88
Drain Target Radius Factor = 0.72
Bottom Clamp Padding = 0.035
Surface Clamp Padding = 0.012

إذا بدك الحركة تقرب أكثر من حافة السطل بدون خروج:
- زيدي Drain Target Radius Factor إلى 0.80 أو 0.85
- زيدي Inside Wall Padding Multiplier إلى 0.92

إذا أي نقطة لسا باينة برا، صغّري:
- Top Radius
- Bottom Radius
أو قللي:
- Inside Wall Padding Multiplier
- Drain Target Radius Factor
