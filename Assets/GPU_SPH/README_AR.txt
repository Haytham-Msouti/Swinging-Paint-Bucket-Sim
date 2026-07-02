نسخة Drop-in لسكربت BucketParticleSystemCustom:

1) احذفي أو استبدلي الملف القديم BucketParticleSystemCustom.cs.
2) ضعي هذا الملف الجديد بنفس الاسم: BucketParticleSystemCustom.cs.
3) ضعي معه:
   - BucketParticleSystemCustomGPU.compute
   - GPUSPHParticleInstanced.shader
4) اعملي Material جديد واختاري Shader:
   Custom/GPUSPHParticleInstanced
5) على نفس GameObject القديم، عبي:
   - Sph Compute = BucketParticleSystemCustomGPU.compute
   - Particle Material = الماتيريال الجديد
   - Emitter = فتحة السطل
   - Tilt Reference = جسم السطل

ملاحظات:
- اسم الكلاس بقي BucketParticleSystemCustom.
- عدد الجزيئات الافتراضي 250000.
- حساب Density / Pressure / Viscosity / Grid / Integration كله على GPU.
- خوارزميات الفتحة والـFlow والـWater Amount والـVisible Liquid بقيت بنفس الفكرة.
- الرسم على الكانفاس صار GPU Paint Bridge حتى لا نقرأ 250 ألف جزيئة من GPU إلى CPU كل فريم.
