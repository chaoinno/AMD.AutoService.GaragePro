import { z } from 'zod'

export const emailSchema = z.string().trim().email('กรุณากรอกอีเมลให้ถูกต้อง')
export const passwordSchema = z.string()
  .min(8, 'รหัสผ่านต้องมีอย่างน้อย 8 ตัวอักษร')
  .regex(/\p{L}/u, 'รหัสผ่านต้องมีตัวอักษรอย่างน้อย 1 ตัว')
  .regex(/\d/, 'รหัสผ่านต้องมีตัวเลขอย่างน้อย 1 ตัว')
export const consentSchema = z.boolean().refine((value) => value, 'กรุณายอมรับข้อกำหนดและนโยบายความเป็นส่วนตัว')
export const otpSchema = z.object({ code: z.string().regex(/^\d{6}$/, 'กรุณากรอกรหัสยืนยัน 6 หลัก') })
export const garageNameSchema = z.string().trim()
  .min(2, 'ชื่ออู่ต้องมีอย่างน้อย 2 ตัวอักษร')
  .max(100, 'ชื่ออู่ต้องไม่เกิน 100 ตัวอักษร')
export const phoneSchema = z.string().transform((value) => value.replace(/[\s-]/g, ''))
  .pipe(z.string().regex(/^(?:0[689]\d{8}|0[2-7]\d{7})$/, 'กรุณากรอกเบอร์โทรศัพท์ไทยให้ถูกต้อง'))
export const sizeSchema = z.enum(['1-3', '4-10', '10+'], { error: 'กรุณาเลือกขนาดอู่' })

export const accountSchema = z.object({
  email: emailSchema,
  password: passwordSchema,
  consent: consentSchema,
  website: z.string(),
})
export const workspaceSchema = z.object({
  garageName: garageNameSchema,
  phone: phoneSchema,
  provinceId: z.number().int().positive('กรุณาเลือกจังหวัด').optional(),
  size: sizeSchema.optional(),
  sampleData: z.boolean().default(true),
})

export type AccountValues = z.infer<typeof accountSchema>
export type OtpValues = z.infer<typeof otpSchema>
export type WorkspaceValues = z.input<typeof workspaceSchema>
