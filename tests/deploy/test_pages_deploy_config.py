from __future__ import annotations

import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
REDIRECTS = REPOSITORY_ROOT / "src" / "ShortLink.Web" / "wwwroot" / "_redirects"
NOT_FOUND = REPOSITORY_ROOT / "src" / "ShortLink.Web" / "wwwroot" / "404.html"
DEPLOY_WORKFLOW = REPOSITORY_ROOT / ".github" / "workflows" / "deploy.yml"


def redirect_lines() -> list[str]:
    return [
        line.strip()
        for line in REDIRECTS.read_text().splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    ]


class PagesRoutingContractTests(unittest.TestCase):
    def test_framework_requests_are_not_caught_by_spa_rewrite(self) -> None:
        sources = [line.split()[0] for line in redirect_lines()]

        self.assertNotIn("/*", sources)

    def test_admin_route_is_rewritten_to_index(self) -> None:
        fields = [line.split() for line in redirect_lines()]

        self.assertIn(["/admin", "/index.html", "200"], fields)

    def test_top_level_404_exists(self) -> None:
        self.assertTrue(NOT_FOUND.is_file())
        self.assertIn("Page not found", NOT_FOUND.read_text())


class PagesDeployWorkflowContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = DEPLOY_WORKFLOW.read_text()

    def test_workflow_pins_current_wrangler(self) -> None:
        self.assertEqual(2, self.workflow.count('wranglerVersion: "4.113.0"'))
        self.assertNotIn('wranglerVersion: "3.90.0"', self.workflow)

    def test_workflow_verifies_preview_before_production(self) -> None:
        expected_order = (
            "name: Verify published Web assets",
            "id: preview_deploy",
            "name: Verify preview Web assets",
            "id: production_deploy",
        )

        positions = [self.workflow.find(marker) for marker in expected_order]

        self.assertNotIn(-1, positions)
        self.assertEqual(sorted(positions), positions)
        self.assertIn("--branch=asset-smoke-${{ github.sha }}", self.workflow)
        self.assertIn(
            "${{ steps.preview_deploy.outputs.deployment-url }}",
            self.workflow,
        )

    def test_workflow_verifies_production(self) -> None:
        production_deploy = self.workflow.find("id: production_deploy")
        production_verification = self.workflow.find(
            "name: Verify production Web assets"
        )

        self.assertGreater(production_deploy, -1)
        self.assertGreater(production_verification, production_deploy)
        self.assertIn(
            "${{ steps.production_deploy.outputs.deployment-url }}",
            self.workflow,
        )


if __name__ == "__main__":
    unittest.main()
