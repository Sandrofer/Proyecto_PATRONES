# Prepares Odoo 20 Community for the POS project. Safe to run several times.
# Runs inside `odoo shell` (variable `env` is provided). See scripts/odoo-setup.ps1.
import base64
import io
import json
from datetime import datetime, timedelta

from PIL import Image, ImageDraw

company = env.company
company.write({'name': 'Cafetería UTA'})


def find_or_create(model, domain, values, update=True):
    record = env[model].search(domain, limit=1)
    if not record:
        return env[model].create(values)
    if update:
        record.write(values)
    return record


def product_image(color, label):
    img = Image.new('RGB', (128, 128), color)
    draw = ImageDraw.Draw(img)
    draw.text((64, 64), label, fill='white', anchor='mm', font_size=40)
    buffer = io.BytesIO()
    img.save(buffer, format='PNG')
    return base64.b64encode(buffer.getvalue()).decode()


# 1. Tax: VAT 15 %, excluded from the price.
tax = env['account.tax'].search([('type_tax_use', '=', 'sale'), ('amount', '=', 15), ('amount_type', '=', 'percent')], limit=1)
tax.write({'name': 'IVA 15%', 'price_include_override': 'tax_excluded'})

# 2. Cash journal and cash payment method.
cash_journal = find_or_create('account.journal', [('code', '=', 'CSH1')],
                              {'name': 'Caja POS', 'type': 'cash', 'code': 'CSH1'}, update=False)
cash_method = find_or_create('pos.payment.method', [('name', '=', 'Efectivo')],
                             {'name': 'Efectivo', 'type': 'cash', 'journal_id': cash_journal.id}, update=False)

# 3. Products. "Producto retirado" is NOT available in the POS (test P04).
products_data = [
    ('CAF-01', 'Café americano', 1.50, True, '#6f4e37', 'CA'),
    ('JUG-01', 'Jugo de naranja natural', 2.25, True, '#e67e22', 'JN'),
    ('SAN-01', 'Sánduche de pollo', 3.75, True, '#c0392b', 'SP'),
    ('CRO-01', 'Croissant', 1.80, True, '#d4a017', 'CR'),
    ('RET-01', 'Producto retirado', 9.99, False, '#7f8c8d', 'X'),
]
products = {}
for code, name, price, in_pos, color, label in products_data:
    template = find_or_create('product.template', [('default_code', '=', code)], {
        'name': name,
        'default_code': code,
        'list_price': price,
        'type': 'consu',
        'is_storable': True,
        'sale_ok': True,
        'available_in_pos': in_pos,
        'taxes_id': [(6, 0, [tax.id])],
        'image_1920': product_image(color, label),
    })
    products[code] = template.product_variant_id

# Odoo's internal "Service Fee" product is not sold by the cashier.
env['product.product'].search([('default_code', '=', 'FEE')]).product_tmpl_id.write({'available_in_pos': False})

warehouse = env['stock.warehouse'].search([('company_id', '=', company.id)], limit=1)
for code, product in products.items():
    if product.qty_available < 20:
        env['stock.quant'].with_context(inventory_mode=True).create({
            'product_id': product.id,
            'location_id': warehouse.lot_stock_id.id,
            'inventory_quantity': 100,
        }).action_apply_inventory()

# 4. Point of sale.
config = find_or_create('pos.config', [('name', '=', 'Cafetería - Caja 1')], {
    'name': 'Cafetería - Caja 1',
    'payment_method_ids': [(6, 0, [cash_method.id])],
})

# 5. Integration user (used only by the .NET API) with POS user rights.
groups_field = 'group_ids' if 'group_ids' in env['res.users']._fields else 'groups_id'
pos_user_group = env.ref('point_of_sale.group_pos_user')
stock_user_group = env.ref('stock.group_stock_user')
user = env['res.users'].with_context(active_test=False).search([('login', '=', 'pos_integration')], limit=1)
if not user:
    user = env['res.users'].create({
        'name': 'Integración API .NET',
        'login': 'pos_integration',
        'company_id': company.id,
        'company_ids': [(6, 0, [company.id])],
    })
# Contact creation: the API registers customers (identified by cedula) in Odoo.
partner_manager_group = env.ref('base.group_partner_manager')
user.write({groups_field: [(4, pos_user_group.id), (4, stock_user_group.id), (4, partner_manager_group.id)]})

# 6. Open a POS session if there is none.
session = env['pos.session'].search([('config_id', '=', config.id), ('state', '!=', 'closed')], limit=1)
if not session:
    session = env['pos.session'].create({'config_id': config.id, 'user_id': user.id})
if session.state == 'opening_control':
    session.set_opening_control(0, 'Apertura automática (setup)')

# 7. API key (scope 'rpc' is required by JSON-2 in Odoo 20). A new one each run; old ones are revoked.
env['res.users.apikeys'].sudo().search([('user_id', '=', user.id), ('name', '=', 'pos-dotnet-api')]).unlink()
api_key = env['res.users.apikeys'].with_user(user)._generate('rpc', 'pos-dotnet-api', datetime.now() + timedelta(days=90))

env.cr.commit()

print('SETUP_RESULT=' + json.dumps({
    'database': env.cr.dbname,
    'pos_config_id': config.id,
    'pos_session_id': session.id,
    'pos_session_name': session.name,
    'pos_session_state': session.state,
    'cash_payment_method_id': cash_method.id,
    'tax_id': tax.id,
    'products': {code: p.id for code, p in products.items()},
    'integration_login': user.login,
    'api_key': api_key,
}))
