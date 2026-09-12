/**
 * Every string the interface shows, in English and Urdu.
 *
 * Kept in one file with the two languages on adjacent lines, so an untranslated string is
 * visible while reading rather than only at runtime. A key is a dotted path by screen.
 *
 * What is deliberately NOT here:
 *
 *  - Data. Product names, customer names and notes are shown exactly as they were typed.
 *    Translating a factory's own product names would be inventing data.
 *  - Money and quantities. Latin digits throughout, including in Urdu: Pakistani business
 *    writing uses them, and a ledger that changes numerals with the interface language is
 *    a reconciliation problem, not a feature.
 *  - Document numbers and codes. D-2609-0001 is an identifier, not prose.
 */

export interface Translation {
  en: string;
  ur: string;
}

export const STRINGS = {
  // ---------------------------------------------------------------- common
  'common.save': { en: 'Save', ur: 'محفوظ کریں' },
  'common.saving': { en: 'Saving…', ur: 'محفوظ ہو رہا ہے…' },
  'common.cancel': { en: 'Cancel', ur: 'منسوخ' },
  'common.close': { en: 'Close', ur: 'بند کریں' },
  'common.confirm': { en: 'Confirm', ur: 'تصدیق کریں' },
  'common.delete': { en: 'Delete', ur: 'حذف کریں' },
  'common.edit': { en: 'Edit', ur: 'ترمیم' },
  'common.add': { en: 'Add', ur: 'شامل کریں' },
  'common.search': { en: 'Search', ur: 'تلاش' },
  'common.filter': { en: 'Filter', ur: 'چھانٹیں' },
  'common.clear': { en: 'Clear', ur: 'صاف کریں' },
  'common.refresh': { en: 'Refresh', ur: 'تازہ کریں' },
  'common.from': { en: 'From', ur: 'سے' },
  'common.to': { en: 'To', ur: 'تک' },
  'common.date': { en: 'Date', ur: 'تاریخ' },
  'common.notes': { en: 'Notes', ur: 'تفصیل' },
  'common.status': { en: 'Status', ur: 'حالت' },
  'common.actions': { en: 'Actions', ur: 'کارروائی' },
  'common.total': { en: 'Total', ur: 'کل' },
  'common.required': { en: 'Required', ur: 'لازمی ہے' },
  'common.none': { en: 'None', ur: 'کوئی نہیں' },
  'common.never': { en: 'Never', ur: 'کبھی نہیں' },
  'common.all': { en: 'All', ur: 'سب' },
  'common.yes': { en: 'Yes', ur: 'ہاں' },
  'common.no': { en: 'No', ur: 'نہیں' },
  'common.active': { en: 'Active', ur: 'فعال' },
  'common.cancelled': { en: 'Cancelled', ur: 'منسوخ شدہ' },
  'common.loading': { en: 'Loading…', ur: 'لوڈ ہو رہا ہے…' },
  'common.page_of': { en: 'Page {page} of {pages}', ur: 'صفحہ {page} از {pages}' },
  'common.rupees': { en: 'PKR', ur: 'روپے' },
  'common.units': { en: 'units', ur: 'عدد' },
  'common.days': { en: 'days', ur: 'دن' },
  'common.hours': { en: 'hours', ur: 'گھنٹے' },

  // ---------------------------------------------------------------- shell
  'app.subtitle': { en: 'Factory management', ur: 'فیکٹری انتظام' },
  'nav.dashboard': { en: 'Dashboard', ur: 'ڈیش بورڈ' },
  'nav.group.floor': { en: 'Factory floor', ur: 'فیکٹری' },
  'nav.production': { en: 'Production', ur: 'پیداوار' },
  'nav.stock': { en: 'Stock', ur: 'گودام' },
  'nav.products': { en: 'Products', ur: 'مصنوعات' },
  'nav.group.sales': { en: 'Sales', ur: 'فروخت' },
  'nav.dispatches': { en: 'Dispatches', ur: 'ڈسپیچ' },
  'nav.customers': { en: 'Customers', ur: 'گاہک' },
  'nav.payments': { en: 'Payments', ur: 'وصولی' },
  'nav.reports': { en: 'Reports', ur: 'رپورٹیں' },
  'nav.group.staff': { en: 'Staff', ur: 'ملازمین' },
  'nav.employees': { en: 'Employees', ur: 'ملازمین' },
  'nav.attendance': { en: 'Attendance', ur: 'حاضری' },
  'nav.payroll': { en: 'Payroll', ur: 'تنخواہ' },
  'nav.group.admin': { en: 'Administration', ur: 'انتظامیہ' },
  'nav.users': { en: 'Users', ur: 'صارفین' },
  'nav.settings': { en: 'Settings', ur: 'ترتیبات' },
  'nav.reason_codes': { en: 'Reason codes', ur: 'وجوہات' },
  'nav.audit': { en: 'Audit', ur: 'ریکارڈ' },

  'account.change_password': { en: 'Change password', ur: 'پاس ورڈ تبدیل کریں' },
  'account.sign_out': { en: 'Sign out', ur: 'باہر نکلیں' },
  'account.label': { en: 'Account', ur: 'اکاؤنٹ' },
  'theme.to_dark': { en: 'Switch to dark', ur: 'گہرا تھیم' },
  'theme.to_light': { en: 'Switch to light', ur: 'روشن تھیم' },
  'lang.switch': { en: 'اردو', ur: 'English' },
  'lang.switch_aria': { en: 'Switch to Urdu', ur: 'Switch to English' },

  // ---------------------------------------------------------------- login
  'login.welcome': { en: 'Welcome back', ur: 'خوش آمدید' },
  'login.subtitle': { en: 'Sign in to continue.', ur: 'جاری رکھنے کے لیے سائن ان کریں۔' },
  'login.username': { en: 'Username', ur: 'صارف نام' },
  'login.password': { en: 'Password', ur: 'پاس ورڈ' },
  'login.submit': { en: 'Sign in', ur: 'سائن ان' },
  'login.submitting': { en: 'Signing in…', ur: 'سائن ان ہو رہا ہے…' },
  'login.enter_username': { en: 'Enter your username', ur: 'اپنا صارف نام لکھیں' },
  'login.enter_password': { en: 'Enter your password', ur: 'اپنا پاس ورڈ لکھیں' },
  'login.forgot': {
    en: 'Forgotten your password? An administrator can set a new one for you.',
    ur: 'پاس ورڈ بھول گئے؟ منتظم آپ کے لیے نیا پاس ورڈ بنا سکتا ہے۔',
  },
  'login.show_password': { en: 'Show password', ur: 'پاس ورڈ دکھائیں' },
  'login.hide_password': { en: 'Hide password', ur: 'پاس ورڈ چھپائیں' },
  'login.session_expired': {
    en: 'Your session has ended. Please sign in again.',
    ur: 'آپ کا سیشن ختم ہو گیا ہے۔ دوبارہ سائن ان کریں۔',
  },
  'login.password_changed': {
    en: 'Your password was changed. Please sign in again.',
    ur: 'آپ کا پاس ورڈ تبدیل ہو گیا ہے۔ دوبارہ سائن ان کریں۔',
  },
  'login.point.stock': {
    en: 'Stock by product and grade, from an append-only ledger',
    ur: 'ہر مصنوع اور درجے کے حساب سے گودام کا مکمل ریکارڈ',
  },
  'login.point.sales': {
    en: 'Dispatches and payments against a running balance',
    ur: 'ڈسپیچ اور وصولی، ہر گاہک کے کھاتے کے ساتھ',
  },
  'login.point.reports': {
    en: 'Kiln output, losses and outstanding debt at a glance',
    ur: 'بھٹی کی پیداوار، نقصان اور واجب الادا رقم ایک نظر میں',
  },
  'login.tagline': { en: 'Management system', ur: 'انتظامی نظام' },
  'login.network_note': {
    en: 'Phase 1 · runs on the factory network',
    ur: 'مرحلہ ۱ · فیکٹری نیٹ ورک پر',
  },

  // ---------------------------------------------------------------- dashboard
  'dash.welcome': { en: 'Welcome, {name}', ur: 'خوش آمدید، {name}' },
  'dash.signed_in_as': { en: 'signed in as {roles}', ur: 'بطور {roles}' },
  'dash.outstanding': { en: 'Outstanding across all customers', ur: 'تمام گاہکوں سے واجب الادا' },
  'dash.owed_by': {
    en: 'owed by {count} account(s)',
    ur: '{count} کھاتوں سے واجب الادا',
  },
  'dash.held_advance': {
    en: 'held in advance across {count} account(s)',
    ur: '{count} کھاتوں میں پیشگی جمع',
  },
  'dash.units_in_stock': { en: 'Units in stock', ur: 'گودام میں موجود' },
  'dash.at_current_rates': { en: '{value} at current rates', ur: 'موجودہ نرخ پر {value}' },
  'dash.produced': { en: 'Produced this month', ur: 'اس ماہ کی پیداوار' },
  'dash.produced_note': {
    en: 'good and seconds, excluding breakages',
    ur: 'اول اور دوم، ٹوٹ پھوٹ کے علاوہ',
  },
  'dash.loss': { en: 'Loss this month', ur: 'اس ماہ نقصان' },
  'dash.within_threshold': { en: 'within the {pct}% threshold', ur: '{pct}% کی حد کے اندر' },
  'dash.above_threshold': { en: 'above the {pct}% threshold', ur: '{pct}% کی حد سے زیادہ' },
  'dash.sales': { en: 'Sales this month', ur: 'اس ماہ فروخت' },
  'dash.sales_note': { en: 'active dispatches only', ur: 'صرف فعال ڈسپیچ' },
  'dash.payments': { en: 'Payments this month', ur: 'اس ماہ وصولی' },
  'dash.payments_note': { en: 'received against accounts', ur: 'کھاتوں میں موصول' },
  'dash.largest_balances': { en: 'Largest balances', ur: 'سب سے بڑے کھاتے' },
  'dash.running_low': { en: 'Running low', ur: 'کم ہو رہا ہے' },
  'dash.see_all': { en: 'See all', ur: 'سب دیکھیں' },
  'dash.nobody_owes': { en: 'Nobody owes anything.', ur: 'کسی کے ذمے کچھ واجب نہیں۔' },
  'dash.nothing_low': { en: 'Nothing is running low.', ur: 'کوئی چیز کم نہیں ہو رہی۔' },
  'dash.customer': { en: 'Customer', ur: 'گاہک' },
  'dash.outstanding_col': { en: 'Outstanding', ur: 'واجب الادا' },
  'dash.last_paid': { en: 'Last paid', ur: 'آخری ادائیگی' },
  'dash.product': { en: 'Product', ur: 'مصنوع' },
  'dash.on_hand': { en: 'On hand', ur: 'موجود' },
  'dash.days_ago': { en: '{days}d ago', ur: '{days} دن پہلے' },
  'dash.in_advance': { en: '{amount} in advance', ur: '{amount} پیشگی' },
  'dash.as_at': {
    en: 'Figures as at {when}. The server caches this for a minute, so it is not second-by-second live.',
    ur: 'اعداد و شمار {when} تک۔ سرور ایک منٹ تک محفوظ رکھتا ہے، اس لیے یہ ہر سیکنڈ تازہ نہیں۔',
  },
  'dash.no_access': { en: 'You do not have access to that page.', ur: 'آپ کو اس صفحے تک رسائی نہیں۔' },

  'grade.First': { en: 'First', ur: 'اول' },
  'grade.Second': { en: 'Second', ur: 'دوم' },
  'grade.Third': { en: 'Third', ur: 'سوم' },

  // ---------------------------------------------------------------- stock
  'stock.title': { en: 'Stock', ur: 'گودام' },
  'stock.subtitle': {
    en: 'What is in the godown, per product and grade.',
    ur: 'گودام میں کیا ہے، ہر مصنوع اور درجے کے حساب سے۔',
  },
  'stock.adjust': { en: 'Adjust stock', ur: 'گودام درست کریں' },
  'stock.grade': { en: 'Grade', ur: 'درجہ' },
  'stock.all_grades': { en: 'All grades', ur: 'تمام درجے' },
  'stock.as_at': { en: 'As at', ur: 'بتاریخ' },
  'stock.blank_live': { en: 'Blank shows live stock', ur: 'خالی چھوڑنے پر موجودہ حالت' },
  'stock.only_in_stock': { en: 'Only in stock', ur: 'صرف موجود' },
  'stock.code': { en: 'Code', ur: 'کوڈ' },
  'stock.name': { en: 'Name', ur: 'نام' },
  'stock.quantity': { en: 'Quantity', ur: 'مقدار' },
  'stock.unit_rate': { en: 'Unit rate', ur: 'فی عدد نرخ' },
  'stock.value': { en: 'Value', ur: 'مالیت' },
  'stock.movements': { en: 'Movement history', ur: 'آمد و رفت' },
  'stock.empty': { en: 'Nothing in stock matching these filters.', ur: 'ان شرائط پر کچھ موجود نہیں۔' },
  'stock.total_value': { en: 'total value', ur: 'کل مالیت' },
  'stock.unpriced_note': {
    en: 'Unpriced stock contributes no value',
    ur: 'بغیر نرخ والے مال کی مالیت شامل نہیں',
  },
  'stock.historical': {
    en: 'Showing the position as at {date}, summed from the movement ledger. Clear the date for the live figure.',
    ur: '{date} کی حالت، کھاتے سے جوڑ کر۔ موجودہ حالت کے لیے تاریخ ہٹا دیں۔',
  },

  // ---------------------------------------------------------------- production
  'prod.title': { en: 'Production', ur: 'پیداوار' },
  'prod.subtitle': {
    en: 'What the kiln delivered, and what broke.',
    ur: 'بھٹی سے کیا نکلا، اور کیا ٹوٹا۔',
  },
  'prod.new': { en: 'New entry', ur: 'نیا اندراج' },
  'prod.fired': { en: 'Fired', ur: 'پکایا' },
  'prod.good': { en: 'Good', ur: 'اول' },
  'prod.seconds': { en: 'Seconds', ur: 'دوم' },
  'prod.broken': { en: 'Broken', ur: 'ٹوٹا' },
  'prod.loss': { en: 'Loss', ur: 'نقصان' },
  'prod.entries': { en: 'Entries', ur: 'اندراجات' },
  'prod.seconds_rate': { en: 'Seconds rate', ur: 'دوم کی شرح' },
  'prod.empty': { en: 'No production entries yet.', ur: 'ابھی کوئی اندراج نہیں۔' },

  // ---------------------------------------------------------------- sales
  'cust.title': { en: 'Customers', ur: 'گاہک' },
  'cust.subtitle': {
    en: 'Who buys, and what each of them owes.',
    ur: 'کون خریدتا ہے، اور کس کے ذمے کتنا ہے۔',
  },
  'cust.new': { en: 'New customer', ur: 'نیا گاہک' },
  'cust.outstanding_tab': { en: 'Outstanding', ur: 'واجب الادا' },
  'cust.all_tab': { en: 'All', ur: 'سب' },
  'cust.statement': { en: 'Statement', ur: 'کھاتہ' },
  'cust.city': { en: 'City', ur: 'شہر' },
  'cust.phone': { en: 'Phone', ur: 'فون' },
  'cust.owing': { en: 'Owing', ur: 'واجب الادا' },
  'cust.empty': { en: 'No customers match these filters.', ur: 'ان شرائط پر کوئی گاہک نہیں۔' },

  'disp.title': { en: 'Dispatches', ur: 'ڈسپیچ' },
  'disp.subtitle': {
    en: 'Goods that left the factory, and what they were billed at.',
    ur: 'فیکٹری سے نکلا مال، اور اس کا بل۔',
  },
  'disp.new': { en: 'New dispatch', ur: 'نیا ڈسپیچ' },
  'disp.number': { en: 'Dispatch', ur: 'ڈسپیچ' },
  'disp.lines': { en: 'Lines', ur: 'اشیاء' },
  'disp.vehicle': { en: 'Vehicle', ur: 'گاڑی' },
  'disp.entered_by': { en: 'Entered by', ur: 'اندراج کرنے والا' },
  'disp.include_cancelled': { en: 'Include cancelled', ur: 'منسوخ شدہ بھی دکھائیں' },
  'disp.empty': { en: 'No dispatches yet.', ur: 'ابھی کوئی ڈسپیچ نہیں۔' },

  'pay.title': { en: 'Payments', ur: 'وصولی' },
  'pay.subtitle': {
    en: 'Money received against customer accounts.',
    ur: 'گاہکوں کے کھاتوں میں موصول رقم۔',
  },
  'pay.new': { en: 'Record payment', ur: 'وصولی درج کریں' },
  'pay.amount': { en: 'Amount', ur: 'رقم' },
  'pay.method': { en: 'Method', ur: 'طریقہ' },
  'pay.reference': { en: 'Reference', ur: 'حوالہ' },
  'pay.empty': { en: 'No payments yet.', ur: 'ابھی کوئی وصولی نہیں۔' },

  // ---------------------------------------------------------------- products
  'prodcat.title': { en: 'Products', ur: 'مصنوعات' },
  'prodcat.subtitle': {
    en: 'What the factory makes, and what each grade sells for.',
    ur: 'فیکٹری کیا بناتی ہے، اور ہر درجے کا نرخ کیا ہے۔',
  },
  'prodcat.new': { en: 'New product', ur: 'نئی مصنوع' },
  'prodcat.prices': { en: 'Prices', ur: 'نرخ' },
  'prodcat.capacity': { en: 'Capacity (ml)', ur: 'گنجائش (ملی لیٹر)' },
  'prodcat.empty': { en: 'No products match these filters.', ur: 'ان شرائط پر کوئی مصنوع نہیں۔' },

  // ---------------------------------------------------------------- reports
  'rep.title': { en: 'Reports', ur: 'رپورٹیں' },
  'rep.subtitle': {
    en: 'Stock, debt, kiln output and sales, for any period.',
    ur: 'گودام، ادھار، پیداوار اور فروخت، کسی بھی مدت کی۔',
  },
  'rep.tab.daily_stock': { en: 'Daily stock', ur: 'روزانہ گودام' },
  'rep.tab.outstanding': { en: 'Outstanding', ur: 'واجب الادا' },
  'rep.tab.production': { en: 'Production', ur: 'پیداوار' },
  'rep.tab.sales': { en: 'Sales', ur: 'فروخت' },
  'rep.group_by': { en: 'Group by', ur: 'ترتیب بلحاظ' },
  'rep.opening': { en: 'Opening', ur: 'ابتدائی' },
  'rep.received': { en: 'Received', ur: 'موصول' },
  'rep.dispatched': { en: 'Dispatched', ur: 'روانہ' },
  'rep.adjusted': { en: 'Adjusted', ur: 'درستی' },
  'rep.closing': { en: 'Closing', ur: 'اختتامی' },
  'rep.pdf': { en: 'PDF', ur: 'پی ڈی ایف' },
  'rep.excel': { en: 'Excel', ur: 'ایکسل' },

  // ---------------------------------------------------------------- employees
  'emp.title': { en: 'Employees', ur: 'ملازمین' },
  'emp.subtitle': {
    en: 'Who works here, and what each is paid per day.',
    ur: 'یہاں کون کام کرتا ہے، اور ہر ایک کی روزانہ اجرت کتنی ہے۔',
  },
  'emp.new': { en: 'New employee', ur: 'نیا ملازم' },
  'emp.edit': { en: 'Edit employee', ur: 'ملازم میں ترمیم' },
  'emp.code': { en: 'Code', ur: 'کوڈ' },
  'emp.name': { en: 'Name', ur: 'نام' },
  'emp.father_name': { en: "Father's name", ur: 'ولدیت' },
  'emp.cnic': { en: 'CNIC', ur: 'شناختی کارڈ' },
  'emp.phone': { en: 'Phone', ur: 'فون' },
  'emp.designation': { en: 'Designation', ur: 'عہدہ' },
  'emp.daily_rate': { en: 'Daily wage', ur: 'روزانہ اجرت' },
  'emp.joined': { en: 'Joined', ur: 'تاریخ تقرری' },
  'emp.active': { en: 'Working', ur: 'کام پر' },
  'emp.inactive': { en: 'Left', ur: 'چھوڑ چکا' },
  'emp.deactivate': { en: 'Mark as left', ur: 'چھوڑ چکا قرار دیں' },
  'emp.empty': { en: 'No employees yet.', ur: 'ابھی کوئی ملازم نہیں۔' },
  'emp.show_left': { en: 'Show those who left', ur: 'چھوڑ جانے والے دکھائیں' },
  'emp.rate_history': { en: 'Wage history', ur: 'اجرت کی تاریخ' },
  'emp.new_rate': { en: 'Change daily wage', ur: 'روزانہ اجرت تبدیل کریں' },
  'emp.effective_from': { en: 'Effective from', ur: 'نافذ العمل از' },
  'emp.rate_note': {
    en: 'The new wage applies from this date onward. Payroll already run keeps the rate it used.',
    ur: 'نئی اجرت اس تاریخ سے لاگو ہوگی۔ پہلے بن چکی تنخواہ اپنے پرانے نرخ پر رہے گی۔',
  },

  // ---------------------------------------------------------------- attendance
  'att.title': { en: 'Attendance', ur: 'حاضری' },
  'att.subtitle': {
    en: 'Who came in today, and for how long.',
    ur: 'آج کون آیا، اور کتنی دیر۔',
  },
  'att.present': { en: 'Present', ur: 'حاضر' },
  'att.half_day': { en: 'Half day', ur: 'آدھا دن' },
  'att.absent': { en: 'Absent', ur: 'غیر حاضر' },
  'att.overtime': { en: 'Overtime', ur: 'اوور ٹائم' },
  'att.overtime_hours': { en: 'Overtime hours', ur: 'اوور ٹائم گھنٹے' },
  'att.mark_all_present': { en: 'Mark everyone present', ur: 'سب کو حاضر لگائیں' },
  'att.save_sheet': { en: 'Save attendance', ur: 'حاضری محفوظ کریں' },
  'att.saved': { en: 'Attendance saved.', ur: 'حاضری محفوظ ہو گئی۔' },
  'att.summary': {
    en: '{present} present, {half} half day, {absent} absent',
    ur: '{present} حاضر، {half} آدھا دن، {absent} غیر حاضر',
  },
  'att.empty': {
    en: 'No employees to mark. Add employees first.',
    ur: 'حاضری لگانے کے لیے کوئی ملازم نہیں۔ پہلے ملازم شامل کریں۔',
  },
  'att.locked_future': {
    en: 'Attendance cannot be marked for a future date.',
    ur: 'آنے والی تاریخ کی حاضری نہیں لگ سکتی۔',
  },
  'att.day_total': { en: 'Day total', ur: 'دن کا کل' },

  // ---------------------------------------------------------------- payroll
  'pr.title': { en: 'Payroll', ur: 'تنخواہ' },
  'pr.subtitle': {
    en: 'A week of attendance turned into what each worker is owed.',
    ur: 'ہفتے بھر کی حاضری کے حساب سے ہر مزدور کی اجرت۔',
  },
  'pr.week': { en: 'Week', ur: 'ہفتہ' },
  'pr.this_week': { en: 'This week', ur: 'یہ ہفتہ' },
  'pr.last_week': { en: 'Last week', ur: 'پچھلا ہفتہ' },
  'pr.preview': { en: 'Preview', ur: 'جائزہ' },
  'pr.run': { en: 'Create payroll', ur: 'تنخواہ بنائیں' },
  'pr.runs': { en: 'Previous runs', ur: 'پچھلی تنخواہیں' },
  'pr.number': { en: 'Payroll', ur: 'تنخواہ نمبر' },
  'pr.employee': { en: 'Employee', ur: 'ملازم' },
  'pr.days_worked': { en: 'Days', ur: 'دن' },
  'pr.full_days': { en: 'Full days', ur: 'پورے دن' },
  'pr.half_days': { en: 'Half days', ur: 'آدھے دن' },
  'pr.ot_hours': { en: 'OT hours', ur: 'اوور ٹائم' },
  'pr.rate': { en: 'Daily wage', ur: 'روزانہ اجرت' },
  'pr.wage_amount': { en: 'Wages', ur: 'اجرت' },
  'pr.ot_amount': { en: 'Overtime pay', ur: 'اوور ٹائم رقم' },
  'pr.net': { en: 'Payable', ur: 'قابل ادائیگی' },
  'pr.empty_preview': {
    en: 'Nobody has attendance in this week, so there is nothing to pay.',
    ur: 'اس ہفتے کسی کی حاضری نہیں، اس لیے کوئی ادائیگی نہیں۔',
  },
  'pr.already_run': {
    en: 'A payroll already covers this week. Cancel it before creating another.',
    ur: 'اس ہفتے کی تنخواہ پہلے بن چکی ہے۔ نئی بنانے سے پہلے اسے منسوخ کریں۔',
  },
  'pr.created': { en: 'Payroll created.', ur: 'تنخواہ بن گئی۔' },
  'pr.cancel': { en: 'Cancel payroll', ur: 'تنخواہ منسوخ کریں' },
  'pr.payslip': { en: 'Payslip', ur: 'تنخواہ پرچی' },
  'pr.workers': { en: '{count} workers', ur: '{count} مزدور' },
  'pr.preview_note': {
    en: 'This is a preview from current attendance. Nothing is saved until you create the payroll.',
    ur: 'یہ موجودہ حاضری کا جائزہ ہے۔ تنخواہ بنانے تک کچھ محفوظ نہیں ہوتا۔',
  },
  'pr.snapshot_note': {
    en: 'Each line keeps the wage rate that applied on the day, so a later rate change never rewrites a payroll already made.',
    ur: 'ہر سطر اسی دن کا نرخ محفوظ رکھتی ہے، اس لیے بعد میں نرخ بدلنے سے پرانی تنخواہ نہیں بدلتی۔',
  },

  // ---------------------------------------------------------------- errors
  'err.network': {
    en: 'The server did not respond. Check that it is running and that the network is up, then try again.',
    ur: 'سرور نے جواب نہیں دیا۔ دیکھیں کہ سرور چل رہا ہے اور نیٹ ورک ٹھیک ہے، پھر دوبارہ کوشش کریں۔',
  },
  'err.unreachable': { en: 'Cannot reach the server', ur: 'سرور تک رسائی نہیں' },
  'err.title': { en: 'Problem', ur: 'مسئلہ' },
  'err.INVALID_CREDENTIALS': {
    en: 'That username and password do not match.',
    ur: 'صارف نام یا پاس ورڈ درست نہیں۔',
  },
  'err.ACCOUNT_LOCKED': {
    en: 'This account is locked after too many failed attempts. Try again in 15 minutes.',
    ur: 'بار بار غلط کوشش کے بعد یہ اکاؤنٹ بند ہے۔ ۱۵ منٹ بعد دوبارہ کوشش کریں۔',
  },
  'err.USER_INACTIVE': {
    en: 'This account has been deactivated. Ask an administrator.',
    ur: 'یہ اکاؤنٹ بند کر دیا گیا ہے۔ منتظم سے رابطہ کریں۔',
  },
  'err.FORBIDDEN': {
    en: 'You are not allowed to do that.',
    ur: 'آپ کو اس کی اجازت نہیں۔',
  },
  'err.VALIDATION_FAILED': {
    en: 'Please check the highlighted fields.',
    ur: 'نشان زد خانے دوبارہ دیکھیں۔',
  },
  'err.CONCURRENCY_CONFLICT': {
    en: 'Somebody else changed this while you were editing. Reload and try again.',
    ur: 'آپ کی ترمیم کے دوران کسی اور نے یہ تبدیل کر دیا۔ دوبارہ لوڈ کر کے کوشش کریں۔',
  },
  'err.STOCK_INSUFFICIENT': {
    en: 'There is not enough stock for this.',
    ur: 'اس کے لیے گودام میں مال کافی نہیں۔',
  },
  'err.reference': { en: 'Reference {id}', ur: 'حوالہ {id}' },
} as const satisfies Record<string, Translation>;

export type StringKey = keyof typeof STRINGS;
