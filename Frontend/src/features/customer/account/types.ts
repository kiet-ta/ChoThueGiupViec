// Wire shapes of .spec/contracts/customers.md (camelCase, UTC ISO-8601).
export type HousingType = 'APARTMENT' | 'HOUSE' | 'ROOM'

export interface Address {
  addressId: number
  label: string
  addressLine: string
  district: string
  city: string
  housingType: HousingType | string
  floorAreaM2: number
  numFloors: number
  totalAreaM2: number
  bedrooms: number | null
  bathrooms: number | null
  latitude: number
  longitude: number
  isDefault: boolean
  createdAt: string
}

/** Body of POST and PUT /customers/me/addresses; `totalAreaM2` is computed by the server and never sent. */
export interface AddressBody {
  label: string
  addressLine: string
  district: string
  city: string
  housingType: HousingType
  floorAreaM2: number
  numFloors: number
  bedrooms: number | null
  bathrooms: number | null
  latitude: number
  longitude: number
  isDefault: boolean
}

export interface FavoriteWorker {
  workerId: number
  fullName: string
  ratingAvg: number
  completedJobs: number
  workStatus: string
  addedAt: string
}

export interface CustomerProfile {
  customerId: number
  phoneNumber: string
  fullName: string
  email: string | null
  trustScore: number
  accountStatus: string
  createdAt: string
}

export interface ProfileBody {
  fullName: string
  email: string | null
}

export interface ReverseGeocodeResult {
  city: string
  district: string
  addressLine: string
  formattedAddress: string
  latitude: number
  longitude: number
}
