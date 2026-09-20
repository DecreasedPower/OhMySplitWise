import { Typography, type TypographyProps } from '@mui/material'
import { formatMoney } from '../domain/money'

export function Money({ value, signed, ...props }: TypographyProps & { value: string; signed?: boolean }) {
  const positive = /^\d+$/.test(value) && BigInt(value) > 0n
  const negative = value.startsWith('-')
  return <Typography {...props} color={negative ? 'error.main' : positive && signed ? 'primary.main' : props.color} sx={{ fontVariantNumeric: 'tabular-nums', ...props.sx }}>
    {signed && positive ? '+' : ''}{formatMoney(value)}
  </Typography>
}
