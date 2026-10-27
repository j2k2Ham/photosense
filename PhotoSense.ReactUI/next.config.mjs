/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  experimental: {
    typedRoutes: true
  },
  // Plain files the service can hand out itself, so that one program is all a computer needs.
  output: 'export'
};

export default nextConfig;
