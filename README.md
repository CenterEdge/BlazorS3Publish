# BlazorS3Publish

`BlazorS3Publish` is a .NET tool that uploads Blazor WebAssembly publish output to an AWS S3 bucket.
It reads the Blazor static web assets endpoints manifest so uploads preserve content metadata (like content type and content encoding), and applies cache-control headers suitable for Blazor assets.

## Install

```bash
dotnet tool install --global BlazorS3Publish
```

## Purpose

Use this tool after `dotnet publish` to deploy the generated `wwwroot` assets to S3 while:

- Uploading files described by the static web assets endpoints manifest.
- Preserving/inferring content type and content encoding (including `.br`/`.gz` assets).
- Applying immutable cache headers for fingerprinted framework assets and `no-cache` for other assets.
- Computing and sending SHA-256 checksums with uploads.

## Usage

```bash
blazor-s3-publish --source <publish-root-or-manifest> --bucket-name <bucket> [options]
```

### Required options

- `--source`
  Path to either:
  - the publish root directory that contains exactly one `*.staticwebassets.endpoints.json`, or
  - a specific `*.staticwebassets.endpoints.json` file.
- `--bucket-name`
  The destination S3 bucket name.

### Optional options

- `--key-prefix`
  Prefix prepended to S3 object keys (for example `production/site`).
- `--max-parallel-uploads`
  Maximum concurrent uploads (`1` to `64`, default `8`).

## Example

```bash
dotnet tool install --global BlazorS3Publish
blazor-s3-publish --source ./publish --bucket-name my-blazor-site --key-prefix production --max-parallel-uploads 16
```

## AWS configuration

The tool uses standard AWS SDK credential resolution (environment variables, shared credentials/config files, IAM role, etc.).
Set `AWS_REGION` to control the target region; if not set, it defaults to `us-east-1`.
