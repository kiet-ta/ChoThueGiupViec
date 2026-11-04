// Static content of the public landing page (ticket #569). Copy comes from the approved mock
// Frontend/ui-template/plandingPageWeb.html.

const unsplash = (id: string, width: number, quality = 72) =>
  `https://images.unsplash.com/photo-${id}?auto=format&fit=max&w=${width}&q=${quality}`

/** Remote photos. Each URL was checked for HTTP 200 when the ticket was implemented. */
export const IMAGES = {
  hero: unsplash('1613490493576-7fde63acd811', 2000),
  bento: unsplash('1527515637462-cff94eecc1ac', 1400),
  cta: unsplash('1560448204-e02f11c3d0e2', 2000),
  living: unsplash('1584622781564-1d987f7333c1', 1400, 70),
  kitchen: unsplash('1556911220-bff31c812dba', 1400, 70),
  bathroom: unsplash('1584622650111-993a426fbf0a', 1400, 70),
  bedroom: unsplash('1522771739844-6a9f6d5f14af', 1400, 70),
} as const

// PLACEHOLDER - replace with real data before launch.
// These figures and quotes have no source in the repository; they exist only so the layout can be reviewed.
export const PLACEHOLDER_PROOF = {
  households: '14.500',
  testimonials: [
    { initials: 'HL', name: 'Chị Hương Ly', place: 'Masteri Thảo Điền', quote: 'Dung dịch thảo mộc thơm dịu như spa. Sàn nhà sạch bóng, con bò thoải mái không sợ hóa chất.' },
    { initials: 'MN', name: 'Anh Minh Nhật', place: 'KĐT Sala, TP. Thủ Đức', quote: 'Nhà 3 tầng điều phối 2 người làm cùng lúc rất gọn gàng. Chỉ hơn 2 tiếng là xong.' },
    { initials: 'TT', name: 'Bác Thanh Thảo', place: 'Vinhomes Central Park', quote: 'Các bạn tự mang máy hút bụi HEPA và khăn 4 màu riêng biệt. Nhà sạch thơm đúng chuẩn.' },
  ],
} as const

export const NAV_LINKS = [
  { href: '#tinh-gia', label: 'Tính giá' },
  { href: '#lich-ca', label: 'Lịch ca' },
  { href: '#cong-viec', label: 'Công việc' },
  { href: '#an-tam', label: 'An tâm' },
  { href: '#bang-gia', label: 'Bảng giá' },
  { href: '#danh-gia', label: 'Đánh giá' },
] as const

export const ROOMS = [
  {
    name: 'Phòng khách',
    image: IMAGES.living,
    alt: 'Phòng khách rộng rãi với sofa và cửa sổ lớn',
    tasks: ['Hút bụi sàn, thảm và khe sofa', 'Lau bề mặt, kệ tivi, tay nắm cửa', 'Lau sàn thảo mộc khử khuẩn'],
  },
  {
    name: 'Nhà bếp',
    image: IMAGES.kitchen,
    alt: 'Nhà bếp sáng với mặt bếp và tủ trắng',
    tasks: ['Lau chùi mặt bếp từ và hút mùi', 'Rửa bồn, lau tủ bếp bên ngoài', 'Khăn màu riêng cho khu vực bếp'],
  },
  {
    name: 'Phòng tắm',
    image: IMAGES.bathroom,
    alt: 'Phòng tắm có vách kính và gương lớn',
    tasks: ['Vệ sinh toilet toàn phần', 'Tẩy cặn canxi vách kính tắm', 'Lau gương, vòi sen, chân tường'],
  },
  {
    name: 'Phòng ngủ',
    image: IMAGES.bedroom,
    alt: 'Phòng ngủ gọn gàng với giường và đèn đầu giường',
    tasks: ['Dọn giường, hút bụi đệm và thảm', 'Lau tủ đầu giường, kệ, cửa lùa', 'Hút bụi khe rãnh và ban công'],
  },
] as const

export const CONTROL_STEPS = [
  { icon: 'shield', title: 'Thợ quen', desc: 'Tự động ưu tiên ghép chuyên viên bạn ưng ý nhất cho tuần kế tiếp.', note: 'Danh tính xác minh bằng CCCD gắn chip.' },
  { icon: 'pin', title: 'Đúng giờ trong 100 m', desc: 'Chuyên viên chỉ nhận ca khi đã đến đúng tòa nhà. Bạn nhận thông báo định vị tức thời.', note: 'Trễ 10 phút được đền 50.000đ.' },
  { icon: 'check', title: 'Nghiệm thu ACCEPT', desc: 'Kiểm tra theo checklist tiêu chuẩn. Vị trí nào chưa sạch được xử lý lại ngay.', note: 'Hài lòng mới thanh toán.' },
] as const

export const PLANS = [
  {
    key: 'studio',
    range: 'Dưới 45 m²',
    name: 'Studio & Mini',
    blurb: 'Người độc thân hoặc cặp đôi bận rộn.',
    price: '180.000',
    meta: '2 giờ · 1 chuyên viên',
    features: ['Dọn giường, hút bụi đệm và thảm', 'Vệ sinh toilet toàn phần', 'Lau sàn thảo mộc khử khuẩn'],
    cta: 'Chọn gói Studio',
    featured: false,
  },
  {
    key: 'apartment',
    range: '45 – 80 m²',
    name: 'Căn hộ chung cư',
    blurb: 'Căn hộ 1–2 phòng ngủ, dọn toàn diện.',
    price: '240.000',
    meta: '3 giờ · 1 chuyên viên',
    features: ['Bao gồm toàn bộ quy trình Studio', 'Tẩy cặn canxi vách kính tắm', 'Hút bụi khe rãnh cửa lùa và ban công', 'Lau mặt bếp từ và hút mùi'],
    cta: 'Đặt lịch ngay',
    featured: true,
  },
  {
    key: 'townhouse',
    range: 'Trên 80 m²',
    name: 'Nhà phố & Biệt thự',
    blurb: 'Tổ đội 2 người, rút ngắn thời gian dọn.',
    price: '480.000',
    meta: '2,5 giờ · 2 chuyên viên',
    features: ['2 chuyên viên chia việc theo sơ đồ tầng', 'Vệ sinh tay vịn cầu thang và chân tường', 'Làm sạch chuyên sâu 2–3 phòng tắm'],
    cta: 'Chọn gói Nhà phố',
    featured: false,
  },
] as const

export const FOOTER_LINKS = ['Cam kết hài lòng', 'Quy chuẩn KYC nhân sự', 'Chính sách bảo hiểm đồ đạc', 'Điều khoản dịch vụ'] as const

/** Milliseconds each room slide stays on screen before the slider advances. */
export const ROOM_SLIDE_MS = 5000
