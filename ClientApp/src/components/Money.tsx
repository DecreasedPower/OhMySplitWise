import { Typography, type TypographyProps } from '@mui/material'
import { formatMoney } from '../domain/money'

export function Money({ value, signed, color, ...props }: TypographyProps & { value: string; signed?: boolean }) {
  const positive = /^\d+$/.test(value) && BigInt(value) > 0n
  const negative = value.startsWith('-')
  return <Typography {...props} color={color ?? (negative ? 'error.main' : positive && signed ? 'primary.main' : undefined)} sx={{ fontVariantNumeric: 'tabular-nums', ...props.sx }}>
    {signed && positive ? '+' : ''}{formatMoney(value)}
  </Typography>
}
