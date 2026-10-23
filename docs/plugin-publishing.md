# Publishing Tracker.Plugins

The package version is `TrackerPluginsVersion` in `Directory.Packages.props`,
independent of the Tracker application version in `package.json`. Bump the package
version when publishing an API change; published versions are immutable.

Before pushing the first plugin-enabled release, configure NuGet.org Trusted
Publishing with these values:

| Field | Value |
| --- | --- |
| Repository owner | `videre-project` |
| Repository | `Tracker` |
| Workflow file | `release.yml` |
| Environment | `release` |
| Package scope | `Tracker.Plugins` |

Choose the NuGet user or organization that should own the package and permit
publishing new packages and new versions. The workflow file is `release.yml`,
including when CI calls it as a reusable workflow. NuGet validates that workflow's
`job_workflow_ref` claim.

In the Tracker GitHub repository, create the `release` environment and add a
`NUGET_USER` secret containing the publishing user's NuGet.org profile username,
using the same account as MTGOSDK if appropriate. This is the profile name, not an
email address or organization name. No long-lived NuGet API key is needed.

CI packs the API and verifies that its DLL is identical to the API DLL in the host
build. After CI succeeds, `release.yml` downloads that exact package artifact,
uses `NuGet/login@v1` to obtain a short-lived credential, publishes to NuGet.org,
then creates the GitHub release. It does not rebuild the package during release.
An existing package version is accepted only after its API DLL is downloaded and
verified against the CI artifact. NuGet upload rejections include the server's
response body in the log; HTTP 409 alone is not treated as a successful duplicate.
The workflow waits up to three minutes for a newly uploaded package to become
available and fails before creating a GitHub release if verification does not pass.

See [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
for policy setup.

## API compatibility and release checks

API generation 1 has a fixed `AssemblyVersion` of `1.0.0.0`. Compatible package
releases retain it; a breaking contract change increments the assembly major and
manifest generation together. Update the supported versions table and sample
package defaults when publishing a new API or SDK dependency. The SDK package
version is recorded in API assembly metadata and checked against plugin dependency
manifests at load time.

Before release, publish the standalone sample against the packed artifact using an
isolated NuGet cache. Verify that a healthy plugin works alongside malformed or
incompatible plugins in the published Windows executable. Exercise installation,
configuration, enablement, update, removal, and restart through Settings → Plugins.
Check `GET /api/plugins/status` for failures and confirm plugin data survives removal.

CI runs the existing frontend tests, builds and publishes Tracker, verifies the
generated API types, and checks the package identity and API assembly against the
host build. The local plugin validation harness is not part of the committed suite.
