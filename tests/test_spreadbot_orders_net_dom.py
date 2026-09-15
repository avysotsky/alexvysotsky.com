from pathlib import Path
import re


HTML = Path(__file__).resolve().parents[1] / 'webclient' / 'dist' / 'coincall-spread-workstation.html'


def page_html():
    return HTML.read_text(encoding='utf-8')


def function_body(html, name):
    start = html.find(f'function {name}(')
    assert start >= 0, f'{name} not found'
    next_function = html.find('\nfunction ', start + 1)
    assert next_function > start, f'{name} end not found'
    return html[start:next_function]


def test_orders_net_inputs_include_spread_size_in_target_order():
    body = function_body(page_html(), 'ccSpreadBotOrdersNetSideInputsHtml')
    return_body = body[body.index("return '<div class=\"cc-spreadbot-side-inputs\""):]
    labels = [
        'data-cc-spreadbot-orders-net-amount',
        'Num of Levels',
        'Spread Size, USD',
        'l1Input',
        'Step, USD',
    ]
    positions = [return_body.index(label) for label in labels]
    assert positions == sorted(positions)
    assert 'data-cc-spreadbot-orders-net-spread-size-usd' in body
    assert 'ccSpreadBotOrdersNetSpreadSizeUsd' in body
    assert 'ccSpreadBotOrdersNetAmountValue()' in body
    assert 'readonly' not in body
    assert 'L1 Price' in body
    assert 'L1 Trail Size, USD' in body
    assert 'data-cc-spreadbot-orders-net-l1-trail-size-usd' in body


def test_step_usd_spin_buttons_are_hidden():
    html = page_html()
    assert '.cc-spreadbot-step-usd-field input::-webkit-outer-spin-button' in html
    assert '.cc-spreadbot-step-usd-field input::-webkit-inner-spin-button' in html
    assert '.cc-spreadbot-step-usd-field input[type=number]' in html
    assert '-moz-appearance:textfield' in html
    assert 'appearance:textfield' in html


def test_orders_net_compact_input_css_preserves_font_size_and_hides_spread_size_spin():
    html = page_html()
    assert '.cc-spreadbot-spread-size-usd-field input::-webkit-outer-spin-button' in html
    assert '.cc-spreadbot-spread-size-usd-field input::-webkit-inner-spin-button' in html
    assert '.cc-spreadbot-spread-size-usd-field input[type=number]' in html

    levels_rule = re.search(r'\.cc-spreadbot-levels-field input \{([^}]*min-height:[^}]+)\}', html)
    price_rule = re.search(
        r'\.cc-spreadbot-spread-size-usd-field input,\s*'
        r'\.cc-spreadbot-l1-price-field input,\s*'
        r'\.cc-spreadbot-step-usd-field input \{([^}]*min-height:[^}]+)\}',
        html,
    )
    assert levels_rule, 'levels input CSS rule not found'
    assert price_rule, 'Orders Net price input CSS rule not found'
    for rule in (levels_rule.group(1), price_rule.group(1)):
        assert 'min-height:20px' in rule
        assert 'padding:2px 6px' in rule
        assert 'font:600 12px/1.1 Segoe UI, sans-serif' in rule
        assert 'font-size:' not in rule


def test_orders_net_algo_info_rows_behavior_is_preserved():
    body = function_body(page_html(), 'ccSpreadBotAlgoInfoHtml')
    assert '<th>Level</th><th>Symbol</th><th>Side</th><th>Amount</th><th>Price</th>' in body
    assert 'for(let i = 1; i <= levels; i++)' in body
    assert "const symbol = ccSpreadBotSpotLabel();" in body
    assert "const amount = ccSpreadBotOrdersNetAmountValue() + ' BTC';" in body
    assert "'<tr><td>' + i + '</td><td>' + ccEsc(symbol) + '</td><td>Buy</td><td>' + ccEsc(amount) + '</td><td>'" in body
    assert 'ccSpreadBotOrdersNetLevelPrices(levels, ccSpreadBotOrdersNetL1Price, ccSpreadBotOrdersNetStepUsd)' in body
    assert '1Min Spread' not in body


def test_spreadbot_log_state_cells_are_normal_weight():
    html = page_html()
    assert '.cc-spreadbot-log-table tbody td:nth-child(2) { font-weight:400; }' in html


def test_orders_net_panel_state_trim_does_not_touch_logs():
    html = page_html()
    formatter = function_body(html, 'ccSpreadBotOrdersNetPanelStateText')
    render = function_body(html, 'ccSpreadsRenderLegDetailsAutomationState')
    footer = function_body(html, 'ccSpreadBotOrdersNetFooterHtml')
    log = function_body(html, 'ccSpreadBotLog')

    assert "'SpreadBot_OrNet_LLF_SS_'" in formatter
    assert "'SpreadBot_OrNet_SLF_SS_'" in formatter
    assert 'if(text.startsWith(prefix)) return text.slice(prefix.length);' in formatter
    assert 'el.textContent = ccSpreadBotOrdersNetPanelStateText(ccSpreadBotOrdersNetStateWord());' in render
    assert 'ccSpreadBotOrdersNetPanelStateText(ccSpreadBotOrdersNetStateWord())' in footer
    assert 'ccSpreadBotOrdersNetPanelStateText' not in log
    assert 'state:state||ccSpreadsLegDetailsAutomationWord()' in log


if __name__ == '__main__':
    test_orders_net_inputs_include_spread_size_in_target_order()
    test_step_usd_spin_buttons_are_hidden()
    test_orders_net_compact_input_css_preserves_font_size_and_hides_spread_size_spin()
    test_orders_net_algo_info_rows_behavior_is_preserved()
    test_spreadbot_log_state_cells_are_normal_weight()
    test_orders_net_panel_state_trim_does_not_touch_logs()
    print('ok')
