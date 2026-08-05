"""PricingConfig 费用估算测试。

价格档位来自 providers.yaml（参考阿里云百炼官网 qwen3.7-flash 定价）：
输入 ≤32K 0.2 元、32K~256K 0.6 元、256K~1M 1.2 元；输出同档位 0.8/2.4/4.8 元。
"""

from app.core.provider_config import PricingConfig, PricingTier


def _make_pricing() -> PricingConfig:
    return PricingConfig(
        input_tiers=[
            PricingTier(max_input_tokens=32768, price_per_million=0.2),
            PricingTier(max_input_tokens=262144, price_per_million=0.6),
            PricingTier(max_input_tokens=1048576, price_per_million=1.2),
        ],
        output_tiers=[
            PricingTier(max_input_tokens=32768, price_per_million=0.8),
            PricingTier(max_input_tokens=262144, price_per_million=2.4),
            PricingTier(max_input_tokens=1048576, price_per_million=4.8),
        ],
    )


def test_estimate_low_tier():
    pricing = _make_pricing()
    # 输入 10k token、输出 2k token（≤32K 档）
    cost = pricing.estimate_cost_cny(10_000, 2_000)
    assert cost is not None
    assert abs(cost - (10_000 / 1_000_000 * 0.2 + 2_000 / 1_000_000 * 0.8)) < 1e-9


def test_estimate_mid_tier():
    pricing = _make_pricing()
    # 输入 50k token（32K~256K 档：0.6/2.4）
    cost = pricing.estimate_cost_cny(50_000, 5_000)
    assert cost is not None
    assert abs(cost - (50_000 / 1_000_000 * 0.6 + 5_000 / 1_000_000 * 2.4)) < 1e-9


def test_estimate_high_tier_overflow():
    pricing = _make_pricing()
    # 超过最大档位时按最后一档计费
    cost = pricing.estimate_cost_cny(2_000_000, 10_000)
    assert cost is not None
    assert abs(cost - (2_000_000 / 1_000_000 * 1.2 + 10_000 / 1_000_000 * 4.8)) < 1e-9


def test_estimate_output_tier_follows_input_tokens():
    pricing = _make_pricing()
    # 输出档位按「单次请求输入 token 数」划分：输入 50k → 输出按 2.4 元档
    cost_50k = pricing.estimate_cost_cny(50_000, 1_000)
    cost_10k = pricing.estimate_cost_cny(10_000, 1_000)
    assert cost_50k is not None and cost_10k is not None
    assert cost_50k > cost_10k


def test_estimate_without_tiers_returns_none():
    pricing = PricingConfig()
    assert pricing.estimate_cost_cny(100, 100) is None


def test_from_mapping_parses_providers_yaml_shape():
    raw = {
        "currency": "CNY",
        "input_tiers": [
            {"max_input_tokens": 32768, "price_per_million": 0.2},
            {"max_input_tokens": 262144, "price_per_million": 0.6},
        ],
        "output_tiers": [
            {"max_input_tokens": 32768, "price_per_million": 0.8},
        ],
    }
    pricing = PricingConfig.from_mapping(raw)
    assert pricing is not None
    assert pricing.currency == "CNY"
    assert len(pricing.input_tiers) == 2
    assert pricing.input_tiers[0].max_input_tokens == 32768
    assert pricing.estimate_cost_cny(10_000, 1_000) is not None


def test_from_mapping_tolerates_garbage():
    assert PricingConfig.from_mapping(None) is None
    assert PricingConfig.from_mapping("nope") is None
    assert PricingConfig.from_mapping({}) is not None


def test_expand_env_var_placeholder():
    """providers.yaml 中的 ${VAR} 占位符应展开为环境变量值。"""
    import os
    from unittest.mock import patch

    from app.core.provider_config import ProviderConfigManager, _expand_env_var

    with patch.dict(os.environ, {"TEST_FAKE_KEY": "sk-fake-from-env"}, clear=False):
        assert _expand_env_var("${TEST_FAKE_KEY}") == "sk-fake-from-env"
        assert _expand_env_var("   ${TEST_FAKE_KEY}  ") == "sk-fake-from-env"
        # 未定义变量 → 空串（不会把字面量当 key）
        assert _expand_env_var("${NOT_DEFINED_ANYWHERE}") == ""
        # 非占位符原样保留
        assert _expand_env_var("plain-value") == "plain-value"
